namespace TimeMemoria.Windows;

/// <summary>
/// Upcoming, in-progress or just-finished maintenance, plus the last window
/// before it. Pulled out of <c>MainWindow.DrawMaintenanceSection</c> so Native
/// can show it too -- Docs/native-parity.md is about the two windows drifting
/// once already, and this is the fix pattern <see cref="StoryEstimate"/> set:
/// shared arithmetic, per-window rendering.
/// </summary>
public static class MaintenanceStatus
{
  public enum State { ServersDown, Upcoming, Completed }

  /// <param name="Remaining">"Back in 2h 10m" / "Starts in 1d 3h". Null once
  /// <see cref="State.Completed"/> -- there is nothing left to count down.</param>
  public sealed record Current(State State, string Title, string? Remaining, string? StartsAt, string? EndsAt, string? Url);

  public sealed record LastWindow(string Title, string? Ended, string? Url);

  /// <param name="Current">Null when nothing is scheduled.</param>
  public sealed record Result(Current? Current, LastWindow? Last);

  public static Result Build(NewsEvent data)
  {
    Current? current = null;

    if (data.Maintenance is MaintenanceWindow m)
    {
      long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      bool upcoming = m.Start.HasValue && m.Start.Value > now;
      bool serversDown = m.Start.HasValue && m.Start.Value <= now && m.End.HasValue && m.End.Value > now;

      // Three states, because the useful number differs in each: how long until
      // it starts, how long until servers return, or nothing once it is over.
      State state = serversDown ? State.ServersDown : upcoming ? State.Upcoming : State.Completed;

      string? remaining = serversDown
        ? $"Back in {TimeFormat.Span(TimeSpan.FromSeconds(m.End!.Value - now))}"
        : upcoming
          ? $"Starts in {TimeFormat.Span(TimeSpan.FromSeconds(m.Start!.Value - now))}"
          : null;

      current = new Current(
        state,
        m.Title ?? "Maintenance",
        remaining,
        m.Start.HasValue ? $"Starts: {TimeFormat.UnixLocal(m.Start.Value)}" : null,
        m.End.HasValue ? $"Ends:   {TimeFormat.UnixLocal(m.End.Value)}" : null,
        m.Url);
    }

    LastWindow? last = data.LastMaintenance is MaintenanceWindow lm
      ? new LastWindow(lm.Title ?? "Maintenance", lm.End.HasValue ? $"Ended: {TimeFormat.UnixLocal(lm.End.Value)}" : null, lm.Url)
      : null;

    return new Result(current, last);
  }
}
