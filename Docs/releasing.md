# Releasing

Order matters: the repository manifest carries a version and a download link, so
it can only be written once the release it points at exists.

## Settled before the first release

Both done; kept because the reasoning still applies.

- **The repository is public.** Required twice over — AGPL-3.0 obligates source
  availability to anyone the plugin is distributed to, and an aggregator fetching
  `repo.json` over HTTP simply cannot read a private repository.
- **`assets/` is the plugin's own artwork**, not QuestTracker's. The icon was
  hand-drawn in `2d52503` and given an alpha channel in `da2d9ae`; the
  screenshots were retaken in `9c6b586`. `icon.png` is 512x512.

## Every release

Now that the plugin is published, **any fix that reaches users is a release**.
There is no such thing as "just a commit" for a behaviour change: whoever
installed it has the previous build, and only a higher version tells Dalamud to
replace it. Bump before committing, not after.

1. Set `<Version>` in `TimeMemoria.csproj`. Format is `MAJOR.MINOR.PATCH.API` —
   the last digit is the Dalamud API level, so 3.0.0 on API 15 is `3.0.0.15`.
2. Commit and push.
3. Tag it and push the tag:

   ```
   git tag v3.0.0.15
   git push origin v3.0.0.15
   ```

   CI builds Release and attaches `latest.zip` to a GitHub release. The tag must
   start with `v` or the release step is skipped.
4. Regenerate and commit the repository manifest:

   ```
   dotnet build -c Release --no-incremental
   python tools/Build-RepoJson.py "What changed, in a sentence."
   git add repo.json && git commit -m "release: 3.0.0.15" && git push
   ```

   `repo.json` copies everything from the manifest DalamudPackager writes, so
   the version there always matches the build. Its download links point at
   `releases/latest`, which resolves to the newest release on its own and never
   needs editing.

   **`--no-incremental` is not optional.** An ordinary build leaves
   `bin/Release/TimeMemoriaV3/TimeMemoriaV3.json` untouched when only the
   version changed, so `Build-RepoJson.py` happily reads the *previous*
   version and writes a manifest advertising it. Nothing errors. The release
   exists on GitHub, the download link resolves to it, and every installer
   decides there is no update — because `AssemblyVersion` is what Dalamud
   compares. This happened to 3.0.3.15.

   **The script now refuses this.** It reads `<Version>` from
   `TimeMemoria.csproj` and exits non-zero, without touching `repo.json`, if the
   built manifest disagrees — so a stale `bin/` stops the release instead of
   shipping an invisible one. The instruction that used to live here was to
   compare the printed version against the tag by eye, which is the kind of check
   that holds until the first time someone is in a hurry.

## Release cadence

At most one published release per rolling 24 hours.

This is not a rule Dalamud enforces. It comes from guidance in the
[dev release channel](https://ptb.discord.com/channels/581875019861328007/1013091333004599397)
on the Discord, which suggests **23 hours 45 minutes as a minimum** gap between
releases. We use 24 hours and a second, so the gap is unambiguously over the line
under any reading and no arithmetic is needed to check it.

The reason to keep to it regardless of what is enforced: every release is an
update prompt and a game restart for people who are not you. Fixes batch and
wait. They do not each earn a version.

## When a change does not appear in game

Three separate things can serve stale code, and none of them says so:

1. **An installed copy shadows the dev plugin.** Both claim the same
   `InternalName` and the local one wins, so the installer keeps reporting the
   dev build's version while the published one goes unnoticed.
2. **A dev plugin reload can serve a cached assembly.** Disabling and
   re-enabling is not always enough — remove the dev plugin entry in
   `/xlsettings` and add it back.
3. **`dotnet build` without `--no-incremental`** leaves DalamudPackager's
   manifest untouched when only the version changed, so `repo.json` advertises
   the previous release.

If a change is definitely in the source and definitely not in the game, work
through those three before looking for the fault in the code. All three have
cost an hour each.

## Verifying a release actually reached users

Disable the dev plugin first. A dev plugin registered from `bin/Release` and an
install from the repository share an `InternalName`, and the local one wins —
so the installer keeps reporting whatever the dev build says while the published
version goes unnoticed. Nothing warns about the collision.

Then check the two things that can disagree:

```
gh api repos/LegendsOfTheGame/TimeMemoriaV3/releases/latest --jq .tag_name
curl -s "https://raw.githubusercontent.com/LegendsOfTheGame/TimeMemoriaV3/main/repo.json?cb=$(date +%s)" | head -6
```

The cache-buster matters — `raw.githubusercontent.com` caches each URL for about
five minutes, so a scan straight after pushing the manifest can legitimately see
the previous version. That is not a failure; wait and rescan.

## Listing it somewhere

The manifest lives at:

```
https://raw.githubusercontent.com/LegendsOfTheGame/TimeMemoriaV3/main/repo.json
```

That URL is what anyone adds to Dalamud's custom repository list, and what
aggregators want. [WilliamW1979/FFXIV](https://github.com/WilliamW1979/FFXIV)
keeps a plain `RepoList.txt` of such URLs and merges them every five minutes —
submitting means getting one line added to that file, by issue or by pull
request.

## Submitting to the official repository

Separate process, and stricter: a pull request to
[goatcorp/DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17).
Disclose AI involvement in the PR description — see [AI-DECLARATION.md](../AI-DECLARATION.md)
for the wording, which is already written.

**This is the eventual destination.** The custom repository is where the plugin
lives until then, not instead of it — and Dalamud's own docs say the project
offers minimal support to custom-repository plugins.

A D17 submission is a directory, not a manifest we generate:

```
MyPluginName/
├── manifest.toml     # repository, commit, owners, project_path, changelog
└── images/
    ├── icon.png      # 1:1, between 64px and 512px
    └── image1..3.png # optional
```

`manifest.toml` points at a **public commit hash**, so updating the plugin there
means opening a PR that changes `commit` — the release cadence stops being
entirely ours and starts including review latency. That is the real cost of
submitting, and the only one outstanding.

`images/` is already satisfied: `assets/icon.png` is 512x512, which is 1:1 and at
the top of the permitted 64–512px range, and the three window screenshots can go
in as `image1..3.png`. Nothing needs redrawing.

### Why there is no testing channel in `repo.json`

D17 has two tracks, and they are directories in that repository: `stable/` and
`testing/live/`. New plugins **must** be submitted to `testing` first. Promotion
is copying the manifest directory from one to the other in a PR; a version bump
is not required, because Dalamud installs the highest `AssemblyVersion` it can
see either way.

A custom repository can imitate this with the testing keys —
`TestingAssemblyVersion`, `DownloadLinkTesting`, `TestingChangelog`,
`IsTestingExclusive` — which serve a second build to users who have opted in to
testing plugins. We deliberately do not. Making it work means publishing testing
builds as GitHub prereleases (so `releases/latest` stays parked on the last
stable release) and teaching `Build-RepoJson.py` to write a tag-specific testing
link, which is the one field in the manifest that could no longer be written once
and forgotten. All of it is D17's job, rebuilt by hand, on the unsupported path,
to be deleted on submission.

So `DownloadLinkTesting` in `repo.json` points at the stable zip. That is inert
while no `TestingAssemblyVersion` exists — Dalamud only uses the testing link
when there is a testing version greater than the release version — but it is a
loaded gun: setting `TestingAssemblyVersion` alone would hand testers the stable
build labelled as the test version, the same silent mismatch as 3.0.3.15. Do not
set one without the other.
