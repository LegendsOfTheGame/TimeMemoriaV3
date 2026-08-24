using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// Character, playtime, pacing and world state — the same things the ImGui News
/// tab gathers.
///
/// Rows are pooled and rewritten each update rather than rebuilt, since most of
/// this changes as you play: pacing moves with every quest, and the feed lines
/// change when it next polls.
/// </summary>
public class NewsPanelNode : TabPanelNode
{
  private const float RowHeight = 20.0f;
  private const float LabelWidth = 190.0f;
  private const float ValueWidth = 260.0f;

  /// <summary>
  /// Size of the row pool. Was 26, which fit what this panel used to show and
  /// nothing more; Story Remaining adds a row per unfinished expansion. The
  /// headroom is deliberate — Maintenance and the per-event end dates are the
  /// next parity pass and land in this same panel.
  /// </summary>
  private const int MaxRows = 48;

  private static readonly Vector4 Heading = new(0.6f, 0.8f, 1.0f, 1.0f);
  private static readonly Vector4 Normal = new(1.0f, 1.0f, 1.0f, 1.0f);
  private static readonly Vector4 Muted = new(0.6f, 0.6f, 0.6f, 1.0f);

  public required IPlaytimeService Playtime { get; init; }
  public required IPacingService Pacing { get; init; }
  public required IFestivalService Festivals { get; init; }
  public required INewsService News { get; init; }
  public required IPlayerState PlayerState { get; init; }
  public required IDataService DataService { get; init; }
  public required ILogger Logger { get; init; }

  private readonly ScrollingNode<VerticalListNode> _scroll;
  private readonly List<(HorizontalListNode Container, TextNode Label, TextNode Value)> _rows = [];

  /// <summary>Rows drawn last time, so the scroll range is only re-measured when it moved.</summary>
  private int _lastRowCount = -1;

  /// <summary>Latched, because Refresh runs every frame and an unlatched log would flood.</summary>
  private bool _overflowLogged;

  public NewsPanelNode()
  {
    // Scrolled rather than sized to fit. The content is genuinely variable —
    // expansions drop out of Story Remaining as they are finished, festivals
    // come and go — and the panel is handed a fixed rectangle by the addon,
    // which at the minimum window height is around eighteen rows. Nothing here
    // clips, so without scrolling the surplus does not vanish quietly: it draws
    // over the window's own chrome.
    //
    // ContentNode properties are set before Size, as ScrollingNode requires.
    _scroll = new ScrollingNode<VerticalListNode> { IsVisible = true, AutoHideScrollBar = true };

    // FitContents makes the list measure only its *visible* children, which is
    // what makes hiding spare rows shrink the scroll range instead of leaving
    // empty space to scroll through.
    _scroll.ContentNode.ItemSpacing = 1.0f;
    _scroll.ContentNode.FitContents = true;
    _scroll.AttachNode(this);

    for (int i = 0; i < MaxRows; i++)
    {
      HorizontalListNode container = new()
      {
        Size = new Vector2(LabelWidth + ValueWidth, RowHeight),
        IsVisible = true
      };

      TextNode label = MakeText(LabelWidth, AlignmentType.Left);
      TextNode value = MakeText(ValueWidth, AlignmentType.Left);

      container.AddNode(label);
      container.AddNode(value);

      _scroll.ContentNode.AddNode(container);
      _rows.Add((container, label, value));
    }

    // Here rather than in OnShown, which would throw away the player's scroll
    // position every time they tabbed away and came back.
    _scroll.ScrollToStart();
  }

  public override void Refresh()
  {
    News.Poll();

    int row = 0;

    SetHeading(ref row, "Character");

    if (PlayerState.IsLoaded)
    {
      Set(ref row, "Commendations", PlayerState.PlayerCommendations.ToString());
      Set(ref row, "Custom Deliveries", $"rank {PlayerState.DeliveryLevel}");

      List<string> standing = [];
      if (PlayerState.IsBattleMentor) standing.Add("Battle Mentor");
      if (PlayerState.IsTradeMentor) standing.Add("Trade Mentor");
      if (PlayerState.IsMentor && standing.Count == 0) standing.Add("Mentor");
      if (PlayerState.IsNovice) standing.Add("Novice");
      if (PlayerState.IsReturner) standing.Add("Returner");

      Set(ref row, "Status", standing.Count > 0 ? string.Join(", ", standing) : "—");
    }
    else
    {
      Set(ref row, "No character loaded", "", muted: true);
    }

    SetHeading(ref row, "Playtime");

    PlaytimeRecord? record = Playtime.Current;
    if (record is not null && record.LifetimePlaytime > TimeSpan.Zero)
    {
      TimeSpan total = record.LifetimePlaytime;
      Set(ref row, "Lifetime", $"{(int)total.TotalDays}d {total.Hours}h {total.Minutes}m");

      // The figure is exactly as old as the last /playtime, and this plugin
      // will not run that for you.
      Set(ref row, "recorded",
        record.LifetimePlaytimeUpdatedUtc.HasValue
          ? Ago(DateTime.UtcNow - record.LifetimePlaytimeUpdatedUtc.Value)
          : "age unknown",
        muted: true);
    }
    else
    {
      Set(ref row, "Lifetime", "run /playtime once to record it", muted: true);
    }

    SetHeading(ref row, "Pacing");

    // "Counting since" rather than "session started": this is when the baseline
    // was taken, which a plugin reload resets. Calling it the start of the
    // session would be a small lie on any day the plugin was reloaded.
    if (Pacing.CountingSince is { } since) Set(ref row, "Counting since", since.ToString("HH:mm"));

    Set(ref row, $"This session ({Pacing.SessionQuests})", Rate(Pacing.SessionMinutesPerQuest));
    Set(ref row, "Overall", Rate(Pacing.OverallMinutesPerQuest));

    // What the overall figure is drawn from. Without it "15m per quest" is a
    // number with nothing behind it — it could be six quests or six thousand,
    // and those are not the same claim.
    Set(ref row, "sample",
      Pacing.HasLifetimePlaytime
        ? $"across {Pacing.TotalComplete} completed quests"
        : "Run /playtime once to enable overall pacing.",
      muted: true);

    Set(ref row, "Main Scenario", Rate(Pacing.MsqMinutesPerQuest));

    // Straight after Pacing, because the two are one thought: a rate, then what
    // that rate buys. Active Events stays last as the one section with no upper
    // bound on its length, so every fixed row above it keeps a stable position.
    SetHeading(ref row, "Story Remaining");

    StoryEstimate.Result story = StoryEstimate.Build(DataService.MsqProgress, Pacing.MsqMinutesPerQuest);

    if (story.Complete)
    {
      Set(ref row, "", "Every Main Scenario quest is complete.", muted: true);
    }
    else
    {
      if (story.Gate is { } gate) Set(ref row, "", gate, muted: true);

      // Two leading spaces mark these as subordinate to the heading, standing in
      // for the indentation the ImGui window bakes into its own strings.
      foreach (StoryEstimate.Line line in story.Lines)
        Set(ref row, $"  {line.Name}", $"{line.Left}   {line.Estimate}");

      if (story.Total is { } total)
      {
        Set(ref row, "", total, muted: true);
        Set(ref row, "", story.TotalTail ?? "", muted: true);
      }
      else
      {
        Set(ref row, "", "Run /playtime once to enable estimates.", muted: true);
      }
    }

    SetHeading(ref row, "Active Events");

    List<ActiveFestival> festivals = Festivals.GetActive();
    if (festivals.Count == 0)
    {
      Set(ref row, "Nothing running", "", muted: true);
    }
    else
    {
      // Phase 0 is the ordinary state — most events never have another one, so
      // printing it reads as an error rather than as information. Only the
      // events that actually progress through stages say anything here.
      foreach (ActiveFestival festival in festivals)
        Set(ref row, festival.DisplayName,
          festival.Phase > 0 ? $"running — stage {festival.Phase}" : "running");
    }

    // The container has to go, not just its text. FitContents measures visible
    // children, so a container left visible around two hidden labels still
    // claims its full height — which is exactly how the companion window's
    // scroll range came to describe thirty-four rows when seventeen were in use.
    int used = row;

    for (; row < _rows.Count; row++)
    {
      (HorizontalListNode container, TextNode label, TextNode value) = _rows[row];
      container.IsVisible = label.IsVisible = value.IsVisible = false;
    }

    // Guarded on the count because Refresh runs every frame and re-measuring
    // walks all forty-eight children. The scroll range only becomes wrong when
    // the number of rows moves, which is when an expansion finishes or a
    // festival starts — not between two frames of standing still.
    if (used != _lastRowCount)
    {
      _lastRowCount = used;
      _scroll.RecalculateSizes();
    }
  }

  private void SetHeading(ref int row, string text)
  {
    if (Exhausted(row)) return;

    (HorizontalListNode container, TextNode label, TextNode value) = _rows[row++];

    container.IsVisible = true;

    label.IsVisible = true;
    label.String = text;
    label.TextColor = Heading;

    value.IsVisible = false;
  }

  private void Set(ref int row, string name, string text, bool muted = false)
  {
    if (Exhausted(row)) return;

    (HorizontalListNode container, TextNode label, TextNode value) = _rows[row++];

    container.IsVisible = label.IsVisible = value.IsVisible = true;

    label.String = name;
    value.String = text;

    label.TextColor = value.TextColor = muted ? Muted : Normal;
  }

  /// <summary>
  /// Whether the pool has run out, which should never happen at forty-eight rows.
  ///
  /// It says so when it does. Both row primitives used to return silently here,
  /// so the panel simply ended early and nothing on screen or in the log
  /// suggested anything was missing — which is how the old ceiling of
  /// twenty-six went unnoticed until the two windows were compared by hand.
  /// </summary>
  private bool Exhausted(int row)
  {
    if (row < _rows.Count) return false;

    if (!_overflowLogged)
    {
      _overflowLogged = true;
      Logger.Debug($"[News] Row pool exhausted at {_rows.Count}; the panel is showing less than it has.");
    }

    return true;
  }

  /// <summary>
  /// A pace, in the same words the ImGui window uses.
  ///
  /// This panel had its own formatter, rounded to the minute. That was harmless
  /// while the number appeared once, and stopped being harmless when Story
  /// Remaining began quoting the same rate three rows below it: "28m per quest"
  /// directly above "at your rate of 28m 16s per quest" is one panel visibly
  /// disagreeing with itself about a figure it computed once.
  /// </summary>
  private static string Rate(double? minutes)
    => minutes is null or <= 0 ? "—" : PacingService.Format(minutes.Value);

  private static string Ago(TimeSpan span)
  {
    if (span < TimeSpan.Zero || span.TotalMinutes < 1) return "just now";
    if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
    if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";

    return $"{(int)span.TotalDays}d ago";
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    // Only the frame. The content node's width is the scroller's business and
    // its height is FitContents', so setting either here would fight them.
    _scroll.Size = new Vector2(Width, Height);
    _scroll.Position = new Vector2(0.0f, 0.0f);
  }

  private static TextNode MakeText(float width, AlignmentType alignment) => new()
  {
    Size = new Vector2(width, RowHeight),
    String = "",
    AlignmentType = alignment,
    FontSize = 12,
    TextColor = Normal,
    IsVisible = true
  };
}
