namespace TimeMemoria.Types;

/// <summary>
/// Where a festival-gated quest stands, derived from Quest.Festival plus the
/// set of festivals this install has ever seen active — see
/// <see cref="TimeMemoria.Services.IFestivalService"/> and
/// <c>festival-gate-design.md</c> for why a set rather than a high-water mark.
/// </summary>
public enum SeasonalAvailability
{
  /// <summary>Quest.Festival is 0 — not seasonal at all.</summary>
  NotSeasonal,

  /// <summary>The gating festival is running right now.</summary>
  Available,

  /// <summary>The gating festival ran before and is not running now. Certain.</summary>
  Missed,

  /// <summary>
  /// Never observed active. Could be upcoming, could be years off — no claim
  /// is made about which.
  /// </summary>
  NotYetAvailable
}
