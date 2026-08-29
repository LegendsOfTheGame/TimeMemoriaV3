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
///
/// The two facts a sweep correlates -- who is playing (IPlayerState) and what
/// is complete (QuestManager) -- do not update on the same frame during a
/// character switch. IsLoggedIn and IsLoaded both read true in that window,
/// which is why they are not enough on their own: on 28 Aug 2026 a sweep fired
/// 0.6s before the Login event, resolved the incoming character's journal, and
/// filled it with the outgoing character's completions. So the sweep waits for
/// Login proper plus a settling delay, and re-checks the character afterwards,
/// discarding anything gathered across a change.
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

  /// <summary>
  /// How long after Login to leave the game alone. Long enough that the quest
  /// tree and the player belong to the same character, short enough to be
  /// invisible next to a 30s sweep.
  /// </summary>
  private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(10);

  private QuestJournal? _journal;
  private string? _characterId;
  private DateTime _nextSweep = DateTime.MinValue;
  private bool _dirty;

  /// <summary>
  /// Set by the Login event and cleared by Logout, rather than polled. Polled
  /// state turns true partway through a character switch; the events do not.
  /// </summary>
  private bool _ready;

  /// <summary>
  /// The completed count seen by the previous sweep while a journal is still
  /// empty, or -1 for "not measured yet".
  ///
  /// A first sighting is the one reading that can never be retried -- it decides
  /// which quests are marked as predating the plugin, and Collect skips anything
  /// already recorded, so whatever it misses is stamped with a real date on the
  /// next sweep and looks like it was played that day. Committing it needs the
  /// count to hold still across two sweeps, so a still-filling tree is not
  /// mistaken for the finished one.
  /// </summary>
  private int _settledCount = -1;

  public int ObservedCount =>
    _journal is null ? 0 : _journal.Completed.Count((e) => e.Value != _journal.PriorDate);

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _framework.Update += OnFrameworkUpdate;
    _clientState.Login += OnLogin;
    _clientState.Logout += OnLogout;

    // Loading while already in game is normal for a dev build and for an
    // update installed mid-session; there is no Login event coming in that
    // case, so treat it as one.
    if (_clientState.IsLoggedIn) OnLogin();

    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _framework.Update -= OnFrameworkUpdate;
    _clientState.Login -= OnLogin;
    _clientState.Logout -= OnLogout;
    Save();
    return _logger.ServiceLifecycle();
  }

  public string? GetCompletionDate(uint questId)
    => _journal is not null && _journal.Completed.TryGetValue(questId, out string? date) ? date : null;

  public bool IsPriorToTracking(string? date)
    => date is not null && _journal is not null && date == _journal.PriorDate;

  private void OnLogin()
  {
    // The previous character's journal must not survive into this session even
    // if Logout was missed -- a stale one is exactly how another character's
    // completions get written to the wrong file.
    _journal = null;
    _characterId = null;
    _dirty = false;
    _settledCount = -1;

    _ready = true;
    _nextSweep = DateTime.UtcNow + SettleDelay;
  }

  private void OnLogout(int type, int code)
  {
    _ready = false;
    Save();
    _journal = null;
    _characterId = null;
  }

  private void OnFrameworkUpdate(IFramework framework)
  {
    if (!_ready) return;
    if (!_clientState.IsLoggedIn || !_playerState.IsLoaded) return;
    if (DateTime.UtcNow < _nextSweep) return;
    _nextSweep = DateTime.UtcNow + SweepInterval;

    try
    {
      string? identity = CurrentCharacterId();
      if (identity is null) return;

      if (_journal is null || _characterId != identity) Load(identity);
      if (_journal is null) return;

      Sweep(identity);
      if (_dirty) Save();
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "[Journal] Sweep failed");
    }
  }

  /// <summary>
  /// Who the game says is playing right now, or null if it will not say yet.
  /// </summary>
  private string? CurrentCharacterId()
  {
    string name = _playerState.CharacterName;
    if (string.IsNullOrEmpty(name)) return null;

    string? world = _playerState.HomeWorld.ValueNullable?.Name.ToString();
    if (string.IsNullOrEmpty(world)) return null;

    return $"{name}@{world}";
  }

  private void Sweep(string identity)
  {
    if (_journal is null) return;

    List<uint> newlyComplete = [];
    HashSet<uint> known = [];
    HashSet<uint> complete = [];
    Collect(_dataService.QuestData, newlyComplete, known, complete);

    // Nothing seen means the tree has not been built yet, or belongs to a
    // character whose data is not resident -- not that nothing is complete. In
    // either case the reading says nothing, and pruning on it would empty the
    // journal.
    if (complete.Count == 0) return;

    // The walk is not instantaneous and the framework thread is not the only
    // thing moving. If the player changed underneath it, everything gathered
    // describes someone else.
    if (CurrentCharacterId() != identity)
    {
      _logger.Debug($"[Journal] Discarded a sweep: {identity} became {CurrentCharacterId() ?? "nobody"} mid-walk.");
      _journal = null;
      _characterId = null;
      _dirty = false;
      return;
    }

    // Adding is safe against a half-filled reading -- a quest seen complete is
    // complete -- but removing is not, so every deletion below waits for the
    // count to hold still across two sweeps. A tree still filling reads as a
    // character who has done less, and acting on that would delete real history.
    bool settled = _settledCount == complete.Count;
    _settledCount = complete.Count;

    if (settled)
    {
      // A quest the game says is unfinished cannot have a completion date, so
      // any entry claiming one is wrong and is dropped -- which is what lets a
      // journal recover from a bad write instead of carrying it forever. Only
      // ids the tree actually covers can be judged.
      List<uint> stale = [.. _journal.Completed.Keys.Where((id) => known.Contains(id) && !complete.Contains(id))];
      foreach (uint id in stale)
        _journal.Completed.Remove(id);

      if (stale.Count > 0)
      {
        _dirty = true;
        _logger.Debug($"[Journal] Dropped {stale.Count} entr(ies) for quests the game reports unfinished.");
      }
    }

    if (newlyComplete.Count == 0) return;

    bool firstSighting = _journal.Completed.Count == 0;

    // A first sighting is the one reading that cannot be retried, so it waits
    // for the same stability the deletions do.
    if (firstSighting && !settled)
    {
      _logger.Debug($"[Journal] Holding first sighting until the tree settles: {complete.Count} complete.");
      return;
    }

    string date = firstSighting ? _journal.PriorDate : DateTime.Now.ToString("yyyy-MM-dd");

    foreach (uint id in newlyComplete)
      _journal.Completed[id] = date;

    _dirty = true;

    if (firstSighting)
      _logger.Debug($"[Journal] Recorded {newlyComplete.Count} existing completions as {date}.");
    else
      _logger.Debug($"[Journal] {newlyComplete.Count} newly completed quest(s) on {date}.");
  }

  /// <summary>
  /// Walks the tree once, gathering the ids it covers, the ones the game
  /// reports complete, and the ones not yet journalled.
  /// </summary>
  private void Collect(QuestData node, List<uint> into, HashSet<uint> known, HashSet<uint> complete)
  {
    foreach (Types.Quest quest in node.Quests)
    {
      if (quest.Ids.Count == 0) continue;

      uint id = quest.Ids[0];
      known.Add(id);

      if (!_dataService.IsQuestComplete(quest)) continue;
      complete.Add(id);

      if (_journal!.Completed.ContainsKey(id)) continue;
      into.Add(id);
    }

    foreach (QuestData child in node.Categories)
      Collect(child, into, known, complete);
  }

  private string PathFor(string characterId)
  {
    string safe = string.Join("_", characterId.Split(Path.GetInvalidFileNameChars()));
    return Path.Combine(_pluginInterface.ConfigDirectory.FullName, $"journal-{safe}.json");
  }

  private void Load(string identity)
  {
    // Written before the file is touched, so a half-loaded journal can never be
    // saved back under the previous character's name.
    _characterId = identity;
    _journal = null;
    _dirty = false;
    _settledCount = -1;

    _pluginInterface.ConfigDirectory.Create();
    string path = PathFor(identity);

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
