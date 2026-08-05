namespace TimeMemoria.Services;

public interface IPacingService : IHostedService
{
  /// <summary>Quests completed since this session's baseline was taken.</summary>
  int SessionQuests { get; }

  /// <summary>Minutes per quest for this session, or null before anything is finished.</summary>
  double? SessionMinutesPerQuest { get; }

  /// <summary>Minutes per quest across the character's whole history.</summary>
  double? OverallMinutesPerQuest { get; }

  /// <summary>False until the player has run /playtime at least once.</summary>
  bool HasLifetimePlaytime { get; }

  /// <summary>Total quests the tree currently reports as complete.</summary>
  int TotalComplete { get; }

  /// <summary>
  /// Minutes of play per Main Scenario quest completed. Deliberately divides all
  /// playtime by MSQ alone: nobody does the story in isolation, so the levelling,
  /// crafting and side content along the way is genuinely part of what it costs
  /// to get through it.
  /// </summary>
  double? MsqMinutesPerQuest { get; }
}

/// <summary>
/// Descriptive pacing. Two figures, measured differently on purpose:
///
///   Session — quests finished since login, over time played since login.
///             Taken as a delta between two readings, never accumulated.
///   Overall — the character's whole history: /playtime divided by every quest
///             completed.
///
/// The previous plugin kept a running counter and incremented it whenever the
/// completion total moved, which meant a quest tree finishing loading looked
/// identical to a player finishing quests. Nothing here counts events; both
/// figures are subtractions between values read live.
/// </summary>
public class PacingService(ILogger _logger, IFramework _framework, IClientState _clientState, IDataService _dataService, IPlaytimeService _playtime)
  : IPacingService
{
  /// <summary>
  /// Grace period after login before anchoring. The quest tree reads zero until
  /// it has been walked, and the game is still settling immediately after login.
  /// </summary>
  private static readonly TimeSpan AnchorDelay = TimeSpan.FromSeconds(5);

  private DateTime? _anchorDueAt = DateTime.UtcNow + AnchorDelay;

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _framework.Update += OnFrameworkUpdate;
    _clientState.Login += OnLogin;
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
    ResetSession();
    _anchorDueAt = DateTime.UtcNow + AnchorDelay;
  }

  /// <summary>
  /// Anchors the session shortly after login rather than waiting for the window
  /// to be opened. Costs one quest tree walk, once per login -- without it the
  /// baseline is whatever the total happened to be when the user first looked,
  /// so quests completed before that never counted.
  /// </summary>
  private void OnFrameworkUpdate(IFramework framework)
  {
    if (_sessionBaseline is not null || _anchorDueAt is null) return;
    if (!_clientState.IsLoggedIn || DateTime.UtcNow < _anchorDueAt.Value) return;

    _dataService.UpdateQuestData();

    int total = TotalComplete;
    if (total <= 0) return;

    _sessionBaseline = total;
    _anchorDueAt = null;
    _logger.Debug($"[Pacing] Session anchored at {total} completed.");
  }

  /// <summary>
  /// Completion total when this session's clock started. Null until the quest
  /// tree has actually been populated — taking it at login would read zero and
  /// make the whole session look like progress.
  /// </summary>
  private int? _sessionBaseline;

  public int TotalComplete => _dataService.ExpansionProgress.Sum((e) => e.NumComplete);

  public bool HasLifetimePlaytime => _playtime.Current?.LifetimePlaytime > TimeSpan.Zero;

  public int SessionQuests
    => _sessionBaseline is null ? 0 : Math.Max(TotalComplete - _sessionBaseline.Value, 0);

  public double? SessionMinutesPerQuest
  {
    get
    {
      int quests = SessionQuests;
      if (quests <= 0) return null;

      TimeSpan played = _playtime.Current?.SessionPlaytime ?? TimeSpan.Zero;
      if (played <= TimeSpan.Zero) return null;

      return played.TotalMinutes / quests;
    }
  }

  public double? MsqMinutesPerQuest
  {
    get
    {
      TimeSpan lifetime = _playtime.Current?.LifetimePlaytime ?? TimeSpan.Zero;
      if (lifetime <= TimeSpan.Zero) return null;

      int msq = _dataService.MsqProgress.Sum((e) => e.NumComplete);
      if (msq <= 0) return null;

      return lifetime.TotalMinutes / msq;
    }
  }

  public double? OverallMinutesPerQuest
  {
    get
    {
      TimeSpan lifetime = _playtime.Current?.LifetimePlaytime ?? TimeSpan.Zero;
      if (lifetime <= TimeSpan.Zero) return null;

      int total = TotalComplete;
      if (total <= 0) return null;

      return lifetime.TotalMinutes / total;
    }
  }

  /// <summary>Drops the session anchor so the next login starts a fresh count.</summary>
  public void ResetSession()
  {
    _sessionBaseline = null;
    _logger.Debug("[Pacing] Session baseline cleared.");
  }

  /// <summary>"14m 20s per quest".</summary>
  public static string Format(double minutesPerQuest)
  {
    int totalSeconds = (int)Math.Round(minutesPerQuest * 60.0);
    return $"{totalSeconds / 60}m {totalSeconds % 60}s per quest";
  }
}
