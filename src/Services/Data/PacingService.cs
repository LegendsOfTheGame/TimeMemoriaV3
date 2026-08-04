namespace TimeMemoria.Services;

public interface IPacingService
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
public class PacingService(ILogger _logger, IClientState _clientState, IDataService _dataService, IPlaytimeService _playtime)
  : IPacingService
{
  /// <summary>
  /// Completion total when this session's clock started. Null until the quest
  /// tree has actually been populated — taking it at login would read zero and
  /// make the whole session look like progress.
  /// </summary>
  private int? _sessionBaseline;

  public int TotalComplete => _dataService.ExpansionProgress.Sum((e) => e.NumComplete);

  public bool HasLifetimePlaytime => _playtime.Current?.LifetimePlaytime > TimeSpan.Zero;

  public int SessionQuests
  {
    get
    {
      int total = TotalComplete;

      // The tree reports zero until it has been walked at least once, so wait
      // for a real number before anchoring the session.
      if (_sessionBaseline is null)
      {
        if (total > 0 && _clientState.IsLoggedIn)
        {
          _sessionBaseline = total;
          _logger.Debug($"[Pacing] Session baseline set at {total} completed.");
        }

        return 0;
      }

      return Math.Max(total - _sessionBaseline.Value, 0);
    }
  }

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
