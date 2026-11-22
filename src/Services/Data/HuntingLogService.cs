namespace TimeMemoria.Services;

/// <summary>
/// The Hunting Log of every class and Grand Company, as last read. Logs keyed
/// by English class abbreviation ("MRD") or "gc1".."gc3"; each value is the
/// rank (ranks complete) followed by the 40 kill counts (10 tasks x 4
/// monsters) of the current rank.
/// </summary>
[Serializable]
public class HuntLogReading
{
  public Dictionary<string, List<int>> Logs { get; set; } = [];
  public DateTime TakenUtc { get; set; }
}

public interface IHuntingLogService : IHostedService
{
  /// <summary>The last reading for this character, or null if never read.</summary>
  HuntLogReading? Current { get; }
}

/// <summary>
/// Saves the Hunting Log for the export, so Lunar can show every class.
///
/// MemoriaProbe's "huntlog" probe showed on 22 Nov 2026 that MonsterNoteManager
/// holds all twelve logs from login, for every class, whatever the current job.
/// What it could not show is the moment between login and the server's data,
/// when every block may read as zero. So a reading never lowers what is saved:
/// a rank never goes down, and inside one rank a kill count never falls. A
/// higher rank replaces the counts, because the game starts the new rank at
/// zero. Read the same way as the /hunt window (HuntingLogFeature), but always
/// on, because the export does not depend on that optional feature.
/// </summary>
public unsafe class HuntingLogService(
  ILogger _logger,
  Configuration _configuration,
  IFramework _framework,
  IClientState _clientState,
  IPlayerState _playerState,
  IDataManager _dataManager) : IHuntingLogService
{
  private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

  /// <summary>
  /// MonsterNoteManager block order, from Hunty's Data.cs StaticData.JobInMemory
  /// (Infiziert90/Hunty): ClassJob rows for classes, negative numbers for the
  /// three Grand Companies.
  /// </summary>
  public static readonly int[] BlockOwner = [1, 2, 3, 4, 5, 6, 7, 26, -1, -2, -3, 29];

  public const int ClassRanks = 5;
  public const int CompanyRanks = 3;
  private const int Counts = 40;

  private HuntLogReading? _current;
  private DateTime _lastPollUtc = DateTime.MinValue;

  public HuntLogReading? Current => _current;

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _framework.Update += OnFrameworkUpdate;
    _clientState.Login += OnLogin;

    if (_clientState.IsLoggedIn) OnLogin();

    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _framework.Update -= OnFrameworkUpdate;
    _clientState.Login -= OnLogin;

    return _logger.ServiceLifecycle();
  }

  private void OnLogin()
  {
    _current = null;

    if (Key is not { } key) return;
    if (_configuration.HuntLogReadings.TryGetValue(key, out HuntLogReading? stored)) _current = stored;
  }

  private void OnFrameworkUpdate(IFramework framework)
  {
    DateTime now = DateTime.UtcNow;
    if (now - _lastPollUtc < PollInterval) return;
    _lastPollUtc = now;

    if (!_playerState.IsLoaded) return;

    MonsterNoteManager* notes = MonsterNoteManager.Instance();
    if (notes is null) return;

    Dictionary<string, List<int>> read = Read(notes);
    Dictionary<string, List<int>> merged = Merge(_current?.Logs, read);

    // Save only on a real change, as the cosmic reading does.
    if (_current is not null && Same(_current.Logs, merged)) return;

    _current = new HuntLogReading { Logs = merged, TakenUtc = now };
    Persist();
  }

  /// <summary>
  /// Every unlocked class (level above 0) and all three Grand Company logs.
  /// A class that is not unlocked reads 255 in some slots (Rogue on Kaye, 22
  /// Nov 2026), so it is not read at all.
  /// </summary>
  private Dictionary<string, List<int>> Read(MonsterNoteManager* notes)
  {
    Dictionary<string, List<int>> logs = [];
    var jobs = _dataManager.GetExcelSheet<ClassJob>(ClientLanguage.English);

    for (int block = 0; block < BlockOwner.Length; block++)
    {
      int owner = BlockOwner[block];
      string key;

      if (owner < 0)
      {
        key = $"gc{-owner}";
      }
      else
      {
        if (jobs.GetRowOrDefault((uint)owner) is not ClassJob job) continue;
        if (_playerState.GetClassJobLevel(job) <= 0) continue;
        key = job.Abbreviation.ToString();
      }

      MonsterNoteRankInfo info = notes->RankData[block];
      List<int> values = new(Counts + 1) { info.Rank };
      for (int task = 0; task < 10; task++)
        for (int slot = 0; slot < 4; slot++)
          values.Add(info.RankData[task].Counts[slot]);

      logs[key] = values;
    }

    return logs;
  }

  /// <summary>Never lower: see the class summary.</summary>
  private static Dictionary<string, List<int>> Merge(Dictionary<string, List<int>>? saved, Dictionary<string, List<int>> read)
  {
    Dictionary<string, List<int>> result = saved is null ? [] : saved.ToDictionary(p => p.Key, p => p.Value.ToList());

    foreach ((string key, List<int> now) in read)
    {
      if (!result.TryGetValue(key, out List<int>? before) || before.Count != now.Count || now[0] > before[0])
      {
        result[key] = now;
        continue;
      }

      if (now[0] < before[0]) continue;

      for (int i = 1; i < now.Count; i++)
        before[i] = Math.Max(before[i], now[i]);
    }

    return result;
  }

  private static bool Same(Dictionary<string, List<int>> a, Dictionary<string, List<int>> b) =>
    a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out List<int>? other) && p.Value.SequenceEqual(other));

  /// <summary>Keyed exactly as the playtime, achievement and cosmic readings are.</summary>
  private string? Key
  {
    get
    {
      if (!_playerState.IsLoaded) return null;

      string world = _playerState.HomeWorld.ValueNullable?.Name.ToString() ?? "Unknown";
      return $"{_playerState.CharacterName}@{world}";
    }
  }

  private void Persist()
  {
    if (Key is not { } key || _current is null) return;

    _configuration.HuntLogReadings[key] = _current;
    _configuration.Save();
  }
}
