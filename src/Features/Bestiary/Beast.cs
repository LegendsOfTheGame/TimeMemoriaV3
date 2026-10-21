using System.Text.Json.Serialization;

namespace TimeMemoria.Features.Bestiary;

/// <summary>
/// One row of <c>data/bestiary.json</c>, copied from Bestiary Mapper. The game
/// has no table of where each beast is tamed, so this is hand-made data.
/// <see cref="No"/> is the XBMPet row id, the same "No." the Master's Bestiary
/// window shows.
/// </summary>
public class Beast
{
  [JsonPropertyName("No")]
  public int No { get; set; }

  [JsonPropertyName("Name")]
  public string Name { get; set; } = "";

  /// <summary>The enemy the beast is tamed from. Empty for a gourd-only beast.</summary>
  [JsonPropertyName("Source")]
  public string Source { get; set; } = "";

  [JsonPropertyName("Zone")]
  public string? Zone { get; set; }

  [JsonPropertyName("X")]
  public float? X { get; set; }

  [JsonPropertyName("Y")]
  public float? Y { get; set; }

  /// <summary>Where to tame it when that is inside a duty, not a map spot.</summary>
  [JsonPropertyName("DutyNote")]
  public string? DutyNote { get; set; }

  [JsonPropertyName("MinLevel")]
  public int MinLevel { get; set; }

  /// <summary>The Kornago Gourd that unlocks it, where one does.</summary>
  [JsonPropertyName("Gourd")]
  public string? Gourd { get; set; }

  [JsonIgnore]
  public bool HasMapLocation => Zone is not null && X.HasValue && Y.HasValue;

  [JsonIgnore]
  public string LocationText => HasMapLocation
    ? $"{Zone} ({X:0}, {Y:0})"
    : DutyNote ?? Zone ?? "";
}
