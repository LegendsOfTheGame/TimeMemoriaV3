namespace TimeMemoria.Services;

public class TocEntry
{
  public string Patch { get; set; } = "";
  public string Expansion { get; set; } = "";
  public string Role { get; set; } = "";
  public string Name { get; set; } = "";
  public List<uint> Ids { get; set; } = [];
}

public enum UnlockState
{
  Unlocked,
  SpoilerLocked,
  FreeTrialLocked
}

/// <summary>
/// How far through the Main Scenario a character is, expressed as a patch.
/// </summary>
/// <param name="Cleared">
/// Highest patch whose closing quest is done. The MSQ is a single line, so
/// finishing a patch's last quest means everything before it is finished too.
/// </param>
/// <param name="Reached">
/// Highest patch whose opening quest is done — the patch currently being played,
/// which is at most one ahead of <paramref name="Cleared"/>.
/// </param>
public record MsqPatchProgress(string? Cleared, string? Reached);

public interface ITocService : IHostedService
{
  UnlockState GetUnlockState(uint expansionId);
  bool IsTrialAccount { get; }

  /// <summary>Main Scenario position as a patch number rather than a quest count.</summary>
  MsqPatchProgress GetMsqPatchProgress();
}

/// <summary>
/// Decides which expansions a character has actually reached, so unreached story
/// can be kept out of sight.
///
/// The gates come from toc.json, hand-derived and carried over from the previous
/// codebase. The game's tables describe journal structure but not patch
/// boundaries, so there is nothing to generate this from.
/// </summary>
public class TocService(ILogger _logger, Configuration _configuration, IDalamudPluginInterface _pluginInterface)
  : ITocService
{
  /// <summary>toc.json's expansion codes, in ExVersion row order.</summary>
  private static readonly string[] ExpansionCodes = ["ARR", "HW", "SB", "ShB", "EW", "DT"];

  /// <summary>
  /// The free trial covers everything through Stormblood, patch 4.58 — so the
  /// first three expansions. ExVersion row ids line up with that directly.
  /// </summary>
  private const uint LastFreeTrialExpansion = 2;

  /// <summary>Expansion row id to the quest ids that open it.</summary>
  private readonly Dictionary<uint, List<uint>> _gates = [];

  /// <summary>Every Main Scenario patch, in release order, with its bookend quests.</summary>
  private readonly List<MsqPatch> _msqPatches = [];

  private sealed record MsqPatch(string Patch, float Order)
  {
    public List<uint> StartIds { get; init; } = [];
    public List<uint> FinalIds { get; init; } = [];
  }

  /// <summary>
  /// True when the account cannot reach past the trial's ceiling. Read from the
  /// game rather than asked of the player.
  /// </summary>
  public unsafe bool IsTrialAccount
  {
    get
    {
      FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState* state =
        FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
      return state != null && state->IsLoaded && state->MaxExpansion <= LastFreeTrialExpansion;
    }
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    try
    {
      string path = Path.Combine(_pluginInterface.AssemblyLocation.Directory!.FullName, "data", "toc.json");
      if (!File.Exists(path))
      {
        _logger.Error($"[Toc] toc.json not found at {path}; progression gating disabled.");
        return _logger.ServiceLifecycle();
      }

      List<TocEntry> entries = JsonSerializer.Deserialize<List<TocEntry>>(File.ReadAllText(path),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

      // Only the first Start of each expansion matters: reaching an expansion at
      // all is the question, not which patch within it.
      foreach (TocEntry entry in entries)
      {
        if (entry.Role != "Start" || entry.Ids.Count == 0) continue;

        int index = Array.IndexOf(ExpansionCodes, entry.Expansion);
        if (index < 0) continue;

        if (!_gates.ContainsKey((uint)index)) _gates[(uint)index] = entry.Ids;
      }

      BuildMsqPatches(entries);

      _logger.Debug($"[Toc] Loaded {entries.Count} entries, {_gates.Count} expansion gates, " +
                    $"{_msqPatches.Count} MSQ patches ({_msqPatches.FirstOrDefault()?.Patch} to {_msqPatches.LastOrDefault()?.Patch}).");
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "[Toc] Failed to load toc.json; progression gating disabled.");
    }

    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken) => _logger.ServiceLifecycle();

  public UnlockState GetUnlockState(uint expansionId)
  {
    // The trial lock is about what the account owns, so spoiler mode cannot
    // reveal it — there is nothing there to reveal.
    if (_configuration.FreeTrialMode && expansionId > LastFreeTrialExpansion)
      return UnlockState.FreeTrialLocked;

    if (IsReached(expansionId)) return UnlockState.Unlocked;

    return _configuration.SpoilerMode ? UnlockState.Unlocked : UnlockState.SpoilerLocked;
  }

  /// <summary>
  /// Collects the Main Scenario Start and Final rows per patch.
  ///
  /// Ordering is numeric, not alphabetical: "7.3" sorts after "7.10" as text but
  /// before it as a number, and the game will eventually ship an x.10.
  /// </summary>
  private void BuildMsqPatches(List<TocEntry> entries)
  {
    Dictionary<string, MsqPatch> byPatch = [];

    foreach (TocEntry entry in entries)
    {
      if (entry.Role is not ("Start" or "Final") || entry.Ids.Count == 0) continue;
      if (!float.TryParse(entry.Patch, NumberStyles.Float, CultureInfo.InvariantCulture, out float order)) continue;

      if (!byPatch.TryGetValue(entry.Patch, out MsqPatch? patch))
        byPatch[entry.Patch] = patch = new MsqPatch(entry.Patch, order);

      (entry.Role == "Start" ? patch.StartIds : patch.FinalIds).AddRange(entry.Ids);
    }

    _msqPatches.AddRange(byPatch.Values.OrderBy((p) => p.Order));
  }

  /// <summary>
  /// Walks the patch list in order and reports the furthest point reached.
  ///
  /// The data stops at the last patch the previous codebase recorded, so a
  /// character further along than that reads as being at the end of it. That is
  /// a floor, never a wrong answer in the other direction.
  /// </summary>
  public MsqPatchProgress GetMsqPatchProgress()
  {
    string? cleared = null;
    string? reached = null;

    foreach (MsqPatch patch in _msqPatches)
    {
      if (patch.StartIds.Any(QuestManager.IsQuestComplete)) reached = patch.Patch;

      // The final patch on record has no closing quest, because it was the
      // current one when this data was written.
      if (patch.FinalIds.Count > 0 && patch.FinalIds.Any(QuestManager.IsQuestComplete)) cleared = patch.Patch;
    }

    return new MsqPatchProgress(cleared, reached);
  }

  /// <summary>A Realm Reborn is always reachable; the rest need their opening quest.</summary>
  private bool IsReached(uint expansionId)
  {
    if (expansionId == 0) return true;
    if (!_gates.TryGetValue(expansionId, out List<uint>? ids)) return true;

    foreach (uint id in ids)
      if (QuestManager.IsQuestComplete(id)) return true;

    return false;
  }
}
