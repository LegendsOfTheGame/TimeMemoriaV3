namespace TimeMemoria.Types;

/// <summary>
/// Per-character playtime. Deliberately holds no quest counters: the shipping
/// plugin accumulated its own completion tally and drifted, because a tree that
/// finishes loading looks identical to a player finishing quests. Completion is
/// read live from the quest tree instead.
/// </summary>
[Serializable]
public class PlaytimeRecord
{
  /// <summary>"CharacterName@WorldName".</summary>
  public string CharacterId { get; set; } = "";

  /// <summary>
  /// Total time played on this character, as reported by /playtime. The game
  /// exposes this nowhere else, so it only moves when the player runs it.
  /// </summary>
  public TimeSpan LifetimePlaytime { get; set; } = TimeSpan.Zero;

  /// <summary>When LifetimePlaytime was last refreshed, so its age is knowable.</summary>
  public DateTime? LifetimePlaytimeUpdatedUtc { get; set; }

  /// <summary>Time observed by the plugin since this session began.</summary>
  [NonSerialized]
  public TimeSpan SessionPlaytime = TimeSpan.Zero;

  /// <summary>Time observed by the plugin across every session it has watched.</summary>
  public TimeSpan ObservedPlaytime { get; set; } = TimeSpan.Zero;

  /// <summary>
  /// ObservedPlaytime at the moment of the last /playtime reply. Null on records
  /// saved before this field existed, until the player runs /playtime again.
  /// </summary>
  public TimeSpan? ObservedAtLifetimeReading { get; set; }

  /// <summary>
  /// Whether <see cref="EstimateLifetime"/> adds observed time to the last reading.
  /// Methods, not properties, so the config serializer does not write them.
  /// </summary>
  public bool CanEstimateLifetime() =>
    LifetimePlaytime > TimeSpan.Zero && ObservedAtLifetimeReading is { } baseline && ObservedPlaytime >= baseline;

  /// <summary>
  /// The last /playtime figure plus the time observed since that reply.
  ///
  /// MemoriaProbe's playtime probe checked this against /playtime on 12 Nov 2026:
  /// three comparisons, one across a full game exit, all within the one-minute
  /// truncation of /playtime. The game counts only logged-in time, and so does
  /// this. Time played while this plugin is not loaded is missing until the next
  /// /playtime the player runs.
  /// </summary>
  public TimeSpan EstimateLifetime() =>
    CanEstimateLifetime() ? LifetimePlaytime + (ObservedPlaytime - ObservedAtLifetimeReading!.Value) : LifetimePlaytime;

  /// <summary>
  /// Lifetime playtime at the moment the plugin saw the last Main Scenario quest
  /// finish. Null until the plugin sees that happen. A character that was
  /// already complete when the plugin first saw it never gets a value, because
  /// the playtime at that earlier moment cannot be found later.
  /// </summary>
  public TimeSpan? MsqCompletePlaytime { get; set; }

  /// <summary>
  /// The number of Main Scenario quests complete when <see cref="MsqCompletePlaytime"/>
  /// was saved. A patch that adds quests changes the count, which makes the saved
  /// value stale until the player finishes the new quests.
  /// </summary>
  public int? MsqCompleteCount { get; set; }

  [NonSerialized]
  public DateTime LastTickUtc = DateTime.UtcNow;
}
