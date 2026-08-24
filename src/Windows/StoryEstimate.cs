namespace TimeMemoria.Windows;

/// <summary>
/// How much Main Scenario is left, and what that has historically cost in hours.
///
/// Descriptive only -- it reports the rate this character has actually managed,
/// and makes no claim about how fast anyone ought to be.
///
/// This lives outside both windows because both need it. The figures were
/// computed inline inside the Classic window's draw method, which is why the
/// Native window went without them: the arithmetic was not reachable from
/// anywhere else. Docs/native-parity.md exists because the two windows drifted
/// once already, and the answer to drift cannot be a second copy of the
/// plugin's headline number.
///
/// A static helper rather than a service. There is no state, no lifetime and
/// nothing to inject: it is a pure function of the expansion counts and one
/// rate. Putting it behind IHostedService would make sentence-building part of
/// the plugin's service contract, and every consumer would then need the DI
/// graph to ask what is essentially a formatting question.
///
/// <b>Strings come out unindented.</b> Classic indents by baking spaces into its
/// text, since ImGui has no column of its own to sit in; Native puts text in a
/// label column where a leading space is visible and wrong. So indentation
/// belongs to whichever window is drawing, and every string here starts at the
/// first character.
/// </summary>
public static class StoryEstimate
{
  /// <summary>
  /// One expansion's row, kept in three pieces rather than joined.
  ///
  /// Classic draws these as three separate calls in three colours at fixed
  /// column offsets; Native joins them into one value. A pre-joined string would
  /// force Classic to change how it renders a section this change is meant to
  /// leave alone.
  /// </summary>
  public sealed record Line(string Name, string Left, string Estimate);

  /// <param name="Complete">Nothing left anywhere. Every other field is empty.</param>
  /// <param name="Gate">
  /// "129 Main Scenario quests until Dawntrail opens." Null when the expansion in
  /// progress is the last one there is -- there is nothing for it to be a gate to,
  /// and the alternative is a sentence about reaching nothing.
  /// </param>
  /// <param name="Lines">One per expansion with anything left, in order.</param>
  /// <param name="Total">
  /// The summary sentence, or null when no rate is known. Null is the signal to
  /// the caller to say so in its own words -- the arithmetic is shared here, the
  /// wording of its absence is not.
  /// </param>
  /// <param name="TotalTail">
  /// The second half of that sentence. Two fields because Classic draws it as two
  /// lines, and joining them here would change the one section the parity notes
  /// quote verbatim.
  /// </param>
  public sealed record Result(
    bool Complete,
    string? Gate,
    IReadOnlyList<Line> Lines,
    string? Total,
    string? TotalTail);

  public static Result Build(IReadOnlyList<ExpansionProgress> msq, double? rate)
  {
    List<ExpansionProgress> remaining = [.. msq.Where((e) => e.NumComplete < e.Total)];

    if (remaining.Count == 0) return new Result(true, null, [], null, null);

    // The expansion currently in progress is the gate to the next one, so its
    // remaining count doubles as the countdown to whatever comes after.
    ExpansionProgress current = remaining[0];
    int toGate = current.Total - current.NumComplete;
    ExpansionProgress? next = msq.FirstOrDefault((e) => e.Id == current.Id + 1);

    string? gate = next is null
      ? null
      : $"{toGate} Main Scenario quest{(toGate == 1 ? "" : "s")} until {next.Name} opens.";

    List<Line> lines = [];

    foreach (ExpansionProgress expansion in remaining)
    {
      int left = expansion.Total - expansion.NumComplete;

      lines.Add(new Line(
        expansion.Name,
        $"{left} left",
        rate.HasValue ? $"~{FormatHours(left * rate.Value)}" : "—"));
    }

    if (!rate.HasValue) return new Result(false, gate, lines, null, null);

    int totalLeft = remaining.Sum((e) => e.Total - e.NumComplete);

    return new Result(
      false,
      gate,
      lines,
      $"{totalLeft} remaining at your rate of {PacingService.Format(rate.Value)}",
      $"— roughly {FormatHours(totalLeft * rate.Value)} of play.");
  }

  /// <summary>
  /// Minutes as the largest unit that keeps the number small: minutes below an
  /// hour, hours below a day, days after that.
  /// </summary>
  public static string FormatHours(double minutes)
  {
    if (minutes < 60) return $"{(int)minutes}m";
    double hours = minutes / 60.0;
    if (hours < 24) return $"{hours:F1}h";
    return $"{hours / 24.0:F1} days";
  }
}
