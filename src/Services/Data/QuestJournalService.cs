namespace TimeMemoria.Services;

/// <summary>
/// One character's record of when quests were finished. Stored beside the
/// plugin config, one file per character, since completion is per character.
/// </summary>
public class QuestJournal
{
  /// <summary>ISO-8601 date the journal started watching this character.</summary>
  public string StartedOn { get; set; } = "";

  /// <summary>
  /// The date recorded for everything already complete when watching began.
  /// One day before StartedOn, so pre-existing completions sort before anything
  /// actually observed and are distinguishable from it.
  /// </summary>
  public string PriorDate { get; set; } = "";

  /// <summary>Quest id to ISO-8601 completion date.</summary>
  public Dictionary<uint, string> Completed { get; set; } = [];
}

public interface IQuestJournalService : IHostedService
{
  /// <summary>The date a quest was completed, or null if not recorded.</summary>
  string? GetCompletionDate(uint questId);

  /// <summary>True when the date is the placeholder for "already done before we started".</summary>
  bool IsPriorToTracking(string? date);

  /// <summary>Quests observed being completed since watching began.</summary>
  int ObservedCount { get; }
}

/// <summary>
/// Records when quests are completed, because the game does not.
///
/// Nothing can be recovered retroactively — a quest finished before this plugin
/// existed has no date anywhere — so everything already done when watching
/// begins is stamped with a single placeholder date, one day before the start.
/// That keeps every quest dated and sorted correctly while remaining honestly
/// distinguishable from a real observation.
///
/// Detection works on quest ids rather than counts. The previous plugin watched
/// a total and treated any increase as progress, so a quest tree finishing
/// loading looked like the player finishing quests. A quest is either in the
/// completed set or it is not, and an unloaded tree simply yields nothing.
/// </summary>
public class QuestJournalService(
  ILogger _logger,
  IDalamudPluginInterface _pluginInterface,
  IClientState _clientState,
  IPlayerState _playerState,
  IFramework _framework,
  IDataService _dataService) : IQuestJournalService
{
  /// <summary>How often to look for newly completed quests.</summary>
  private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

  private QuestJournal? _journal;
  private string? _characterId;
  private DateTime _nextSweep = DateTime.MinValue;
  private bool _dirty;

  public int ObservedCount =>
    _journal is null ? 0 : _journal.Completed.Count((e) => e.Value != _journal.PriorDate);

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _framework.Update += OnFrameworkUpdate;
    _clientState.Logout += OnLogout;
    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _framework.Update -= OnFrameworkUpdate;
    _clientState.Logout -= OnLogout;
    Save();
    return _logger.ServiceLifecycle();
  }

  public string? GetCompletionDate(uint questId)
    => _journal is not null && _journal.Completed.TryGetValue(questId, out string? date) ? date : null;

  public bool IsPriorToTracking(string? date)
    => date is not null && _journal is not null && date == _journal.PriorDate;

  private void OnLogout(int type, int code)
  {
    Save();
    _journal = null;
    _characterId = null;
  }

  private void OnFrameworkUpdate(IFramework framework)
  {
    if (!_clientState.IsLoggedIn || !_playerState.IsLoaded) return;
    if (DateTime.UtcNow < _nextSweep) return;
    _nextSweep = DateTime.UtcNow + SweepInterval;

    try
    {
      if (_journal is null) Load();
      if (_journal is null) return;

      Sweep();
      if (_dirty) Save();
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "[Journal] Sweep failed");
    }
  }

  private void Sweep()
  {
    if (_journal is null) return;

    List<uint> newlyComplete = [];
    Collect(_dataService.QuestData, newlyComplete);

    // Nothing seen means the tree has not been built yet, not that nothing is
    // complete -- so there is nothing to record either way.
    if (newlyComplete.Count == 0) return;

    bool firstSighting = _journal.Completed.Count == 0;
    string date = firstSighting ? _journal.PriorDate : DateTime.Now.ToString("yyyy-MM-dd");

    foreach (uint id in newlyComplete)
      _journal.Completed[id] = date;

    _dirty = true;

    if (firstSighting)
      _logger.Debug($"[Journal] Recorded {newlyComplete.Count} existing completions as {date}.");
    else
      _logger.Debug($"[Journal] {newlyComplete.Count} newly completed quest(s) on {date}.");
  }

  private void Collect(QuestData node, List<uint> into)
  {
    foreach (Types.Quest quest in node.Quests)
    {
      if (quest.Ids.Count == 0) continue;

      uint id = quest.Ids[0];
      if (_journal!.Completed.ContainsKey(id)) continue;
      if (!_dataService.IsQuestComplete(quest)) continue;

      into.Add(id);
    }

    foreach (QuestData child in node.Categories)
      Collect(child, into);
  }

  private string PathFor(string characterId)
  {
    string safe = string.Join("_", characterId.Split(Path.GetInvalidFileNameChars()));
    return Path.Combine(_pluginInterface.ConfigDirectory.FullName, $"journal-{safe}.json");
  }

  private void Load()
  {
    string world = _playerState.HomeWorld.ValueNullable?.Name.ToString() ?? "Unknown";
    _characterId = $"{_playerState.CharacterName}@{world}";

    _pluginInterface.ConfigDirectory.Create();
    string path = PathFor(_characterId);

    if (File.Exists(path))
    {
      _journal = JsonSerializer.Deserialize<QuestJournal>(File.ReadAllText(path));
      if (_journal is not null) return;
    }

    DateTime today = DateTime.Now.Date;
    _journal = new QuestJournal
    {
      StartedOn = today.ToString("yyyy-MM-dd"),
      PriorDate = today.AddDays(-1).ToString("yyyy-MM-dd")
    };

    _dirty = true;
    _logger.Debug($"[Journal] Started for {_characterId}; prior completions dated {_journal.PriorDate}.");
  }

  private void Save()
  {
    if (_journal is null || _characterId is null) return;

    try
    {
      File.WriteAllText(PathFor(_characterId),
        JsonSerializer.Serialize(_journal, new JsonSerializerOptions { WriteIndented = true }));
      _dirty = false;
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "[Journal] Failed to save");
    }
  }
}
