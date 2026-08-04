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

  [NonSerialized]
  public DateTime LastTickUtc = DateTime.UtcNow;
}
