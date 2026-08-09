namespace TimeMemoria.Services;

public interface ILedgerExportService
{
  /// <summary>Time Memoria's own progression shape.</summary>
  string BuildProgressionJson();

  /// <summary>The Adventurer's Ledger field shape, ready to merge.</summary>
  string BuildLedgerJson();
}

/// <summary>
/// Builds the clipboard payloads. Local only — output goes to the clipboard and
/// nowhere else; the plugin makes no outbound requests.
/// </summary>
public class LedgerExportService(IPluginLog _pluginLog, IPlayerState _playerState, IClassJobProgressService _classJobProgress, IPlaytimeService _playtime, IDataService _dataService, ITocService _tocService)
  : ILedgerExportService
{
  public string BuildProgressionJson()
  {
    try
    {
      JsonArray jobs = [];
      foreach (ClassJobProgress p in _classJobProgress.GetProgress().Where(p => p.IsUnlocked))
      {
        jobs.Add(new JsonObject
        {
          ["name"] = p.Name,
          ["abbreviation"] = p.Abbreviation,
          ["category"] = p.Category,
          ["level"] = p.Level,
          ["exp"] = p.Experience,
          ["expToNext"] = p.ExperienceToNext
        });
      }

      JsonObject root = new()
      {
        ["character"] = _playerState.IsLoaded ? _playerState.CharacterName : string.Empty,
        ["world"] = WorldName(),
        ["exportedUtc"] = DateTime.UtcNow.ToString("o"),
        ["classJobs"] = jobs
      };

      return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    catch (Exception ex)
    {
      _pluginLog.Error(ex, "[LedgerExport] Failed to build progression payload");
      return "{}";
    }
  }

  public string BuildLedgerJson()
  {
    try
    {
      // Forced: an export is a figure someone is about to paste somewhere and
      // keep. Being a few seconds stale is fine on screen and not fine here —
      // an export written into the ledger is wrong until the next one.
      _dataService.UpdateQuestData(true);

      JsonObject root = new()
      {
        ["source"] = "time-memoria",
        ["version"] = typeof(LedgerExportService).Assembly.GetName().Version?.ToString() ?? "unknown",
        ["exported"] = DateTime.UtcNow.ToString("o"),
        ["name"] = _playerState.IsLoaded ? _playerState.CharacterName : string.Empty,
        ["server"] = BuildServer()
      };

      if (_playerState.IsLoaded) root["comm"] = _playerState.PlayerCommendations;

      JsonObject? playtime = BuildPlaytime();
      if (playtime != null) root["playtime"] = playtime;

      List<ClassJobProgress> progress = _classJobProgress.GetProgress();
      root["combat"] = BuildLevels(progress, "combat");
      root["craft"] = BuildLevels(progress, "craft");
      root["gather"] = BuildLevels(progress, "gather");
      root["quests"] = BuildQuests();
      root["msqBreakdown"] = BuildMsqBreakdown();

      JsonObject? msqPatch = BuildMsqPatch();
      if (msqPatch != null) root["msqPatch"] = msqPatch;

      return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    catch (Exception ex)
    {
      _pluginLog.Error(ex, "[LedgerExport] Failed to build ledger payload");
      return "{}";
    }
  }

  /// <summary>
  /// Lifetime playtime with the age of the figure attached. Omitted entirely when
  /// the player has never run /playtime, since sending zeroes would overwrite a
  /// good value the ledger already holds.
  /// </summary>
  private JsonObject? BuildPlaytime()
  {
    PlaytimeRecord? record = _playtime.Current;
    if (record == null || record.LifetimePlaytime <= TimeSpan.Zero) return null;

    JsonObject playtime = new()
    {
      ["days"] = (int)record.LifetimePlaytime.TotalDays,
      ["hours"] = record.LifetimePlaytime.Hours
    };

    if (record.LifetimePlaytimeUpdatedUtc.HasValue)
      playtime["asOf"] = record.LifetimePlaytimeUpdatedUtc.Value.ToString("o");

    return playtime;
  }

  /// <summary>
  /// Completed quests per top-level journal section, in the ledger's own keys.
  ///
  /// These counts were previously typed in by hand off the plugin's Overview,
  /// which meant they were only ever as fresh as the last time someone
  /// remembered to do it.
  ///
  /// Counts are read from the category nodes directly, so the Settings toggles
  /// for excluding Other Quests and Levequests do not reach this. Those change
  /// what the plugin chooses to display; they are not a statement about what the
  /// character has done, and a display preference must not silently rewrite
  /// someone else's stored data.
  /// </summary>
  private JsonObject BuildQuests()
  {
    // The ledger has no bucket for Other Quests, so its Overall is the sum of
    // the six it does track -- and its UI asserts exactly that. Sending our own
    // Overall here would include Other Quests and break that check.
    Dictionary<string, string> keys = new()
    {
      ["Main Scenario"] = "msq",
      ["Chronicles of a New Era"] = "era",
      ["Sidequests"] = "side",
      ["Allied Society Quests"] = "allied",
      ["Class & Job Quests"] = "class",
      ["Levequests"] = "leve"
    };

    Dictionary<string, int> totals = keys.Values.ToDictionary((k) => k, (_) => 0);

    // Sections live under each expansion, so a section's total is the sum of its
    // appearances across all of them.
    foreach (QuestData expansion in _dataService.QuestData.Categories)
      foreach (QuestData category in expansion.Categories)
        if (keys.TryGetValue(category.EnglishTitle, out string? key))
          totals[key] += (int)category.NumComplete;

    JsonObject quests = new() { ["overall"] = totals.Values.Sum() };
    foreach (KeyValuePair<string, string> pair in keys)
      quests[pair.Value] = totals[pair.Value];

    return quests;
  }

  /// <summary>
  /// Story position as a patch number, which is how people describe it to each
  /// other — "I'm on 6.3", not "I have done 812 Main Scenario quests".
  ///
  /// Two values, because they answer different questions. <c>cleared</c> is the
  /// last patch finished and is the safe one to gate on. <c>reached</c> is the
  /// patch being played now, and can be one ahead.
  ///
  /// Omitted entirely when neither is known, rather than sent as nulls that a
  /// consumer might store over a good value.
  /// </summary>
  private JsonObject? BuildMsqPatch()
  {
    MsqPatchProgress progress = _tocService.GetMsqPatchProgress();
    if (progress.Cleared is null && progress.Reached is null) return null;

    JsonObject patch = [];
    if (progress.Cleared is not null) patch["cleared"] = progress.Cleared;
    if (progress.Reached is not null) patch["reached"] = progress.Reached;

    return patch;
  }

  /// <summary>
  /// Completed MSQ per expansion. The tree is nested under expansion, so each one
  /// carries its own Main Scenario section and no patch mapping is needed.
  /// </summary>
  private JsonObject BuildMsqBreakdown()
  {
    Dictionary<uint, string> keys = new()
    {
      [0] = "arr", [1] = "hw", [2] = "stb", [3] = "shb", [4] = "ew", [5] = "dt"
    };

    JsonObject breakdown = [];

    foreach (QuestData expansion in _dataService.QuestData.Categories)
    {
      if (!keys.TryGetValue(expansion.SortKey, out string? key)) continue;

      QuestData? msq = expansion.Categories.FirstOrDefault((c) => c.EnglishTitle == "Main Scenario");
      breakdown[key] = msq != null ? (int)msq.NumComplete : 0;
    }

    return breakdown;
  }

  /// <summary>
  /// Levels the way the ledger stores them: level plus the fraction of the way
  /// through it, to three decimals. Max level emits a bare integer.
  /// </summary>
  private static JsonObject BuildLevels(List<ClassJobProgress> progress, string category)
  {
    JsonObject obj = [];

    foreach (ClassJobProgress job in progress.Where(p => p.Category == category))
    {
      if (job.Level == 0 || job.IsMaxLevel) obj[job.Name] = job.Level;
      else obj[job.Name] = Math.Round(job.Level + job.Fraction, 3, MidpointRounding.AwayFromZero);
    }

    return obj;
  }

  private JsonObject BuildServer()
  {
    JsonObject server = new() { ["pdc"] = "", ["ldc"] = "", ["world"] = "" };
    if (!_playerState.IsLoaded) return server;

    World? world = _playerState.HomeWorld.ValueNullable;
    if (world == null) return server;

    server["world"] = world.Value.Name.ToString();

    // Home world -> data centre -> region group, which is what the ledger calls
    // the physical data centre.
    WorldDCGroupType? dc = world.Value.DataCenter.ValueNullable;
    if (dc == null) return server;

    server["ldc"] = dc.Value.Name.ToString();
    WorldRegionGroup? region = dc.Value.Region.ValueNullable;
    if (region != null) server["pdc"] = region.Value.Name.ToString();

    return server;
  }

  private string WorldName()
  {
    if (!_playerState.IsLoaded) return string.Empty;
    return _playerState.HomeWorld.ValueNullable?.Name.ToString() ?? string.Empty;
  }
}
