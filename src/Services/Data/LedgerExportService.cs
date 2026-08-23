namespace TimeMemoria.Services;

public interface ILedgerExportService
{
  /// <summary>The Adventurer's Ledger field shape, ready to merge.</summary>
  string BuildLedgerJson();
}

/// <summary>
/// Builds the clipboard payloads. Local only — output goes to the clipboard and
/// nowhere else; the plugin makes no outbound requests.
/// </summary>
public class LedgerExportService(IPluginLog _pluginLog, IPlayerState _playerState, IClassJobProgressService _classJobProgress, IPlaytimeService _playtime, IDataService _dataService, ITocService _tocService, IAlliedSocietyService _societies, IAchievementService _achievements)
  : ILedgerExportService
{
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
      root["societies"] = BuildSocieties();
      root["classQuests"] = BuildClassQuests();

      JsonObject? collectables = BuildCollectables();
      if (collectables != null) root["collectables"] = collectables;

      JsonObject? duties = Reading(_achievements.Duties);
      if (duties != null) root["duties"] = duties;

      root["msqBreakdown"] = BuildMsqBreakdown();

      JsonObject? msqPatch = BuildMsqPatch();
      if (msqPatch != null) root["msqPatch"] = msqPatch;

      JsonObject? unlocks = BuildUnlocks();
      if (unlocks != null) root["unlocks"] = unlocks;

      return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    catch (Exception ex)
    {
      _pluginLog.Error(ex, "[LedgerExport] Failed to build ledger payload");
      return "{}";
    }
  }

  /// <summary>
  /// Which features this character has unlocked — see <see cref="LedgerUnlocks"/>
  /// for where the ids come from and why quest completion is the right question.
  ///
  /// Every key is emitted, true or false. An absent key has to mean "this build
  /// did not know about that unlock" so the ledger can fall back to its own patch
  /// gate; if false and absent were the same thing, adding a key later would read
  /// as everyone suddenly unlocking it.
  ///
  /// Null when the player is not loaded. Sending a payload of falses for a
  /// character the client cannot see would tell the ledger to hide everything.
  /// </summary>
  private JsonObject? BuildUnlocks()
  {
    if (!_playerState.IsLoaded) return null;

    JsonObject unlocks = [];

    foreach ((string key, uint[] ids) in LedgerUnlocks.Quests)
      unlocks[key] = ids.Any(QuestManager.IsQuestComplete);

    // Squadrons, the daily Hunt bills and player housing all open at Second
    // Lieutenant, so one rank read gates several ledger rows. Rank is only
    // meaningful once enlisted — GetGrandCompanyRank wants a company to ask
    // about, and an unenlisted character has none.
    uint company = _playerState.GrandCompany.RowId;
    bool enlisted = company != 0;

    unlocks["grandCompany"] = enlisted;
    unlocks["squadron"] = enlisted &&
      _playerState.GetGrandCompanyRank(_playerState.GrandCompany.Value) >= LedgerUnlocks.SecondLieutenantRank;

    return unlocks;
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
  /// Allied society standing, which the ledger has until now asked people to
  /// enter by hand — and which was consequently a rank and three hundred points
  /// out of date in the file that prompted this.
  ///
  /// Rank and points are durable facts about a character and belong here. The
  /// daily allowance deliberately does not: it resets every day, so a stored
  /// snapshot of it is worse than nothing.
  ///
  /// Every society is sent, including untouched ones at rank 0, because "not
  /// started" is a fact the ledger cannot otherwise distinguish from "never
  /// synced".
  ///
  /// Keyed by sheet id rather than by name. There are at least four spellings in
  /// circulation for the same society — the sheet says "sylphs", the wiki says
  /// "Sylphs", the ledger says "Sylph" — and an import that matches on wording
  /// breaks the day any of them is revised. The id is the same number the client
  /// indexes standing by and cannot drift. The name rides along for display.
  /// </summary>
  private JsonArray BuildSocieties()
  {
    JsonArray societies = [];

    foreach (SocietyStanding standing in _societies.GetStandings())
      societies.Add(new JsonObject
      {
        ["id"] = standing.Index,
        ["name"] = standing.Name,
        ["rank"] = standing.Rank,
        ["points"] = standing.Points,
        ["needed"] = standing.Needed
      });

    return societies;
  }

  /// <summary>
  /// Collectables gathered and synthesised — the two counts the ledger's Trade
  /// Mentor section has until now asked people to type in.
  ///
  /// Unlike everything else here these are not free to read. The game keeps no
  /// running counter; the number lives in an achievement and the client only
  /// holds whichever one the player last looked at. So each count carries the
  /// moment it was seen, and the ledger is expected to show that age rather than
  /// present a week-old figure as current.
  ///
  /// <c>exact</c> is false when the tier that was read is already complete: a
  /// finished tier reports its own requirement, not the running total, so the
  /// number is a floor. The distinction matters for a threshold — "300 of 300"
  /// derived from a floor is not the same claim as reaching 300.
  ///
  /// A side never read is omitted entirely rather than sent as zero, for the
  /// same reason playtime is: a zero here would overwrite a good value the
  /// ledger already holds. The whole block goes when neither has been read.
  /// </summary>
  private JsonObject? BuildCollectables()
  {
    JsonObject collectables = [];

    if (Reading(_achievements.Gathered) is { } gathered) collectables["gathered"] = gathered;
    if (Reading(_achievements.Crafted) is { } crafted) collectables["crafted"] = crafted;

    return collectables.Count > 0 ? collectables : null;
  }

  /// <summary>
  /// One achievement-derived figure with everything needed to judge it: the
  /// number, whether it is exact or a floor, and when it was taken. Null when the
  /// series has never been read, so the field is omitted rather than sent as a
  /// zero that would overwrite something better.
  /// </summary>
  private static JsonObject? Reading(AchievementReading? reading) => reading is null ? null : new JsonObject
  {
    ["count"] = reading.Value,
    ["exact"] = reading.IsExact,
    ["asOf"] = reading.TakenUtc.ToString("o")
  };

  /// <summary>
  /// Every completed class, job and role quest, by name.
  ///
  /// The ledger has these as a hand-ticked checkbox per quest, which is a hundred
  /// and fifty clicks describing something the game already knows exactly. A flat
  /// list is enough: the ledger holds its own quest-to-job mapping, so it only
  /// needs to be told which are done.
  ///
  /// Flat also means role quests arrive without being asked for — they share this
  /// category with job quests — so any the ledger chooses to list will start
  /// ticking themselves.
  ///
  /// Completed only. The full list would be several times longer and every entry
  /// absent from this one is, by definition, not done.
  /// </summary>
  private JsonArray BuildClassQuests()
  {
    JsonArray done = [];

    foreach (QuestData expansion in _dataService.QuestData.Categories)
      foreach (QuestData category in expansion.Categories)
        if (category.EnglishTitle == "Class & Job Quests")
          CollectComplete(category, done);

    return done;
  }

  /// <summary>
  /// Hide is deliberately not consulted. It carries the Display setting — "show
  /// only completed" or "show only incomplete" — so under the latter every
  /// completed quest is hidden and reading it here would export an empty list.
  /// What a character has finished is not a display preference.
  /// </summary>
  private void CollectComplete(QuestData node, JsonArray into)
  {
    foreach (Types.Quest quest in node.Quests)
      if (_dataService.IsQuestComplete(quest))
        into.Add(quest.Title);

    foreach (QuestData child in node.Categories) CollectComplete(child, into);
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
