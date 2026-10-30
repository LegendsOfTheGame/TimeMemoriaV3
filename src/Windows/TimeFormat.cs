using System.Globalization;

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

  /// <summary>
  /// When the feed itself was last built, not when the plugin fetched it, so a
  /// stalled GitHub Action shows as an old date. Month by name, so no reader
  /// takes day for month — the forms Haven settled on, 28 Oct 2026.
  /// </summary>
  // Time and date as separate holes: a lone space inside one format string
  // rendered as no gap at all in game. Month names are spelled out here rather
  // than taken from CultureInfo: .NET under Wine can run without ICU data, and
  // then de-DE quietly formats in English.
  public static string FeedUpdated(long unixSeconds, DisplayLanguage language)
  {
    DateTimeOffset local = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
    string time = local.ToString("H:mm", CultureInfo.InvariantCulture);
    int m = local.Month - 1;

    return language switch
    {
      DisplayLanguage.German => $"Zuletzt aktualisiert {time} — {local.Day}. {GermanMonths[m]} {local.Year}",
      DisplayLanguage.French => $"Dernière mise à jour {time} — {local.Day} {FrenchMonths[m]} {local.Year}",
      DisplayLanguage.Japanese => $"最終更新 {time} — {local.Year}年{local.Month}月{local.Day}日",
      _ => $"Last updated {time} — {local.Day} {EnglishMonths[m]} {local.Year}"
    };
  }

  private static readonly string[] EnglishMonths =
    ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

  private static readonly string[] GermanMonths =
    ["Jan.", "Feb.", "März", "Apr.", "Mai", "Juni", "Juli", "Aug.", "Sept.", "Okt.", "Nov.", "Dez."];

  private static readonly string[] FrenchMonths =
    ["janv.", "févr.", "mars", "avr.", "mai", "juin", "juil.", "août", "sept.", "oct.", "nov.", "déc."];

  public static string Span(TimeSpan span)
  {
    if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
    if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
    if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m";
    return "less than a minute";
  }
}
