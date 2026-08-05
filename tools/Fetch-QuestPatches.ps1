<#
.SYNOPSIS
  Builds a questId -> patch map from Garland Tools.

.DESCRIPTION
  Nothing in FFXIV's game files records which patch a quest belongs to. Garland
  Tools carries one per quest, derived from XIVAPI diffing game versions over a
  decade -- a historical record that cannot be recomputed locally, because each
  patch overwrites the files that would prove it.

  This walks the quest ids the plugin has already snapshotted and asks Garland
  for each one's patch, at a deliberate crawl.

  Safe to stop and restart. Results are flushed to disk as they arrive and
  already-known ids are skipped, so a second run only fetches what is missing.

.PARAMETER Snapshot
  quest-snapshot.json written by the plugin. Defaults to the live one.

.PARAMETER Output
  Where to write the map. Re-read on start to resume.

.PARAMETER DelaySeconds
  Gap between calls. Garland asks for 0.88s; do not lower it.

.EXAMPLE
  .\Fetch-QuestPatches.ps1
#>

[CmdletBinding()]
param(
    [string] $Snapshot = "$env:APPDATA\XIVLauncher\pluginConfigs\TimeMemoriaV3\quest-snapshot.json",
    [string] $Output   = "$PSScriptRoot\..\data\quest-patches.json",
    [double] $DelaySeconds = 0.88
)

$ErrorActionPreference = 'Stop'

# Quest rows start at 0x10000. Anything below that in the snapshot is a
# levequest, which lives in a different Garland namespace and is skipped.
$QuestIdFloor = 65536

if (-not (Test-Path $Snapshot)) {
    throw "Snapshot not found at $Snapshot. Load the plugin once to create it."
}

$ids = (Get-Content $Snapshot -Raw | ConvertFrom-Json).QuestIds |
        Where-Object { $_ -ge $QuestIdFloor } |
        Sort-Object

Write-Host "Quests in snapshot: $($ids.Count)"

# ── resume ────────────────────────────────────────────────────────────────────
$map = @{}
if (Test-Path $Output) {
    $existing = Get-Content $Output -Raw | ConvertFrom-Json
    foreach ($p in $existing.PSObject.Properties) { $map[$p.Name] = $p.Value }
    Write-Host "Resuming: $($map.Count) already known"
}

$todo = $ids | Where-Object { -not $map.ContainsKey("$_") }
if ($todo.Count -eq 0) { Write-Host "Nothing to do."; return }

$eta = [TimeSpan]::FromSeconds($todo.Count * $DelaySeconds)
Write-Host "To fetch: $($todo.Count)  (about $([int]$eta.TotalMinutes) minutes)"
Write-Host ""

New-Item -ItemType Directory -Force -Path (Split-Path $Output) | Out-Null

# ── fetch ─────────────────────────────────────────────────────────────────────
$done = 0
$failed = 0
$started = Get-Date

foreach ($id in $todo) {
    try {
        # No www. The www host has a broken database fallback: for records with
        # no pre-generated static file it answers HTTP 200 with a MySQL error as
        # the body, which parses to nothing and looks like a missing patch.
        $url = "https://garlandtools.org/db/doc/quest/en/2/$id.json"
        $res = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 20 `
                                 -Headers @{ 'User-Agent' = 'TimeMemoria-patch-map/1.0' }

        # Garland stores patch as a JSON number, so 2.0 arrives as 2 and 7.55 as
        # 7.55. Kept numeric: bucketing x.yz into x.y is then arithmetic rather
        # than string surgery, and "2" vs "2.0" stops being a distinction.
        # A 200 carrying an error string still reaches here, so check we were
        # actually given a quest rather than trusting the status code.
        if ($null -eq $res.quest) {
            throw "no quest object in response"
        }

        # Garland stores patch as a JSON number, so 2.0 arrives as 2 and 7.55 as
        # 7.55. Kept numeric: bucketing x.yz into x.y is then arithmetic rather
        # than string surgery, and "2" vs "2.0" stops being a distinction.
        $patch = $res.quest.patch
        $map["$id"] = if ($null -ne $patch) { [double]$patch } else { $null }
    }
    catch {
        # Counted rather than silently recorded, so a run that hits trouble says
        # so instead of quietly producing blanks.
        $failed++
        $map["$id"] = $null
        Write-Verbose "$id : $($_.Exception.Message)"
    }

    $done++

    # Flush periodically rather than at the end, so an interrupted run keeps
    # everything it collected.
    if ($done % 50 -eq 0 -or $done -eq $todo.Count) {
        $map | ConvertTo-Json -Depth 2 | Set-Content $Output -Encoding UTF8

        $elapsed = (Get-Date) - $started
        $rate = $done / [Math]::Max($elapsed.TotalSeconds, 1)
        $left = [TimeSpan]::FromSeconds(($todo.Count - $done) / [Math]::Max($rate, 0.001))
        Write-Host ("{0,6}/{1}  {2,5:P0}  no-record: {3,-5}  remaining ~{4:hh\:mm}" -f `
                    $done, $todo.Count, ($done / $todo.Count), $failed, $left)
    }

    Start-Sleep -Seconds $DelaySeconds
}

$map | ConvertTo-Json -Depth 2 | Set-Content $Output -Encoding UTF8

# ── summary ───────────────────────────────────────────────────────────────────
$known = ($map.Values | Where-Object { $_ }).Count
Write-Host ""
Write-Host "Done. $known of $($map.Count) quests have a patch."
Write-Host "Written to $Output"

$byPatch = $map.Values | Where-Object { $null -ne $_ } | Group-Object | Sort-Object { [double]$_.Name }
Write-Host ""
Write-Host "Quests per patch:"
$byPatch | ForEach-Object { Write-Host ("  {0,-6} {1}" -f $_.Name, $_.Count) }
