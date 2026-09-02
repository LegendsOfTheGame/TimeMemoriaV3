namespace TimeMemoria.Windows;

/// <summary>
/// Active and upcoming events, reconciled against what the client itself
/// reports running -- a festival the game has switched on but the feed has no
/// entry for still gets a row, with its own note explaining why it has no end
/// date rather than being dropped silently.
///
/// Pulled out of <c>MainWindow.DrawEventsSection</c> for the same reason as
/// <see cref="MaintenanceStatus"/>: the reconciliation lived only inside
/// Classic's draw method, so Native could not show it. See
/// Docs/native-parity.md.
/// </summary>
public static class EventsSummary
{
  public enum State { Active, Upcoming }

  /// <param name="Timing">"Ends in 7d 18h" / "Starts in 3d". For a festival the
  /// feed never mentioned, the explanation sentence instead of a countdown --
  /// there is no end date to count down to.</param>
  /// <param name="Url">Null for a festival read from the client; the feed
  /// carries a Lodestone link, the client does not.</param>
  public sealed record Line(State State, string Title, string? Timing, string? Url);

  /// <param name="UndocumentedCount">How many <see cref="Lines"/> came from the
  /// client rather than the feed. Zero when every active festival's title
  /// matched something the feed reported.</param>
  public sealed record Result(IReadOnlyList<Line> Lines, int UndocumentedCount)
  {
    public string? Footer => UndocumentedCount == 0
      ? null
      : $"{UndocumentedCount} event{(UndocumentedCount == 1 ? " is" : "s are")} live in game but absent from the news feed.";

    public const string FooterTooltip =
      "The feed only recognises events whose titles match a known list.\nThese were read from the game instead.";
  }

  public static Result Build(NewsEvent data, IReadOnlyList<ActiveFestival> activeFestivals)
  {
    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    List<Line> lines = [];
    List<string> shownTitles = [];

    foreach (GameEvent ev in data.Events)
    {
      bool active = ev.Start.HasValue && ev.Start.Value <= now && ev.End.HasValue && ev.End.Value > now;
      bool upcoming = ev.Start.HasValue && ev.Start.Value > now;
      if (!active && !upcoming) continue;

      if (ev.Title is not null) shownTitles.Add(ev.Title);

      string? timing = active && ev.End.HasValue
        ? $"Ends in {TimeFormat.Span(TimeSpan.FromSeconds(ev.End.Value - now))}"
        : upcoming && ev.Start.HasValue
          ? $"Starts in {TimeFormat.Span(TimeSpan.FromSeconds(ev.Start.Value - now))}"
          : null;

      lines.Add(new Line(active ? State.Active : State.Upcoming, ev.Title ?? "Event", timing, ev.Url));
    }

    // Anything the client has switched on that the feed did not mention.
    List<ActiveFestival> missing = [.. activeFestivals.Where((f) => !shownTitles.Any((t) => Overlaps(t, f.DisplayName)))];

    foreach (ActiveFestival festival in missing)
      lines.Add(new Line(State.Active, festival.DisplayName, "Running now — end date not published to the feed.", null));

    return new Result(lines, missing.Count);
  }

  /// <summary>Loose title match, since feed titles are prose and festival names are short.</summary>
  private static bool Overlaps(string feedTitle, string festivalName)
  {
    // Mapped names are disambiguated by year -- "All Saint's Wake (2026)" --
    // which no feed title carries. Match on the name alone.
    int bracket = festivalName.IndexOf('(');
    if (bracket > 0) festivalName = festivalName[..bracket].TrimEnd();

    if (festivalName.Length < 4) return false;
    return feedTitle.Contains(festivalName, StringComparison.OrdinalIgnoreCase)
        || festivalName.Contains(feedTitle, StringComparison.OrdinalIgnoreCase);
  }
}
