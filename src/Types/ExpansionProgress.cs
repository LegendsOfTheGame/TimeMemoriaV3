namespace TimeMemoria.Types;

/// <summary>
/// Quest completion for a single expansion. Derived from the Quest sheet's own
/// Expansion column rather than a maintained list of patch numbers, so a new
/// expansion appears here on the day its quests ship.
/// </summary>
public class ExpansionProgress
{
  /// <summary>ExVersion row id. 0 is A Realm Reborn.</summary>
  public uint Id { get; init; }

  public required string Name { get; init; }
  public int NumComplete { get; init; }
  public int Total { get; init; }

  public float Fraction => Total > 0 ? NumComplete / (float)Total : 0f;
}
