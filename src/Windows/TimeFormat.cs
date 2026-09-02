namespace TimeMemoria.Windows;

/// <summary>
/// The two time formatters Maintenance and Active Events both need, pulled out
/// once they stopped belonging to a single draw method. See
/// <see cref="MaintenanceStatus"/> and <see cref="EventsSummary"/>.
/// </summary>
public static class TimeFormat
{
  /// <summary>Local time with the year, so a stale feed is obvious rather than ambiguous.</summary>
  public static string UnixLocal(long unixSeconds) =>
    DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("MMM d, yyyy, h:mm tt");

  public static string Span(TimeSpan span)
  {
    if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
    if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
    if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m";
    return "less than a minute";
  }
}
