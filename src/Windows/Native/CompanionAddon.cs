using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// A small window for the things worth knowing while actually playing:
/// playtime, pacing, and which battle jobs are furthest behind.
///
/// Deliberately something you open and close, not something that appears. The
/// plugin does not interrupt play — no toasts, nothing that follows you into a
/// duty, nothing that opens itself. This stays on the right side of that line
/// precisely because it only exists when asked for, and it reads state that is
/// already being tracked rather than watching anything new.
/// </summary>
public unsafe class CompanionAddon : NativeAddon
{
  private const float RowHeight = 20.0f;
  private const float LabelWidth = 132.0f;

  /// <summary>
  /// The scroll bar is drawn over the content rather than beside it, so a row
  /// sized to the full content width loses its last few pixels underneath it —
  /// which clipped the closing bracket off "Stuffed Chysahl (HQ)". Right-aligned
  /// values are the only things that reach that far, so the allowance comes out
  /// of the value column.
  /// </summary>
  private const float ScrollBarWidth = 10.0f;

  private const float ValueWidth = 150.0f - ScrollBarWidth;
  private const float ListSpacing = 2.0f;

  /// <summary>
  /// The ceiling on rows the window will build. Rows past this are silently
  /// dropped, which is how the job list fell off the bottom when collectables
  /// were added — and every allied society capping at once would ask for more
  /// than this on its own, so the limit is real rather than theoretical.
  ///
  /// It is a limit on what gets built, not on what is visible: the content
  /// scrolls, so a tall list is reachable rather than clipped.
  /// </summary>
  private const int MaxRows = 38;

  /// <summary>Beyond a handful this stops being "what should I level next".</summary>
  private const int LowestJobCount = 5;

  private static readonly Vector4 Heading = new(0.6f, 0.8f, 1.0f, 1.0f);
  private static readonly Vector4 Normal = new(1.0f, 1.0f, 1.0f, 1.0f);
  private static readonly Vector4 Muted = new(0.6f, 0.6f, 0.6f, 1.0f);

  public required IClassJobProgressService ProgressService { get; init; }
  public required IPlaytimeService Playtime { get; init; }
  public required IPacingService Pacing { get; init; }
  public required IAchievementService Achievements { get; init; }
  public required IFoodService Food { get; init; }

  /// <summary>
  /// Only used to keep the totals moving. Session pacing is derived from them,
  /// so without this the session reads zero for as long as this window is the
  /// only one open.
  /// </summary>
  public required IDataService DataService { get; init; }

  public required IAlliedSocietyService Societies { get; init; }

  public required Configuration Config { get; init; }

  /// <summary>
  /// Raised by the title bar's gear button, which trades this window for the
  /// full one. The addon owns neither side of that swap, so it asks.
  /// </summary>
  public required System.Action OnSwapRequested { get; init; }

  private ScrollingNode<VerticalListNode>? _scroll;
  private TextureButtonNode? _swapButton;
  private readonly List<(HorizontalListNode Container, TextNode Label, TextNode Value)> _rows = [];

  protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
  {
    base.OnSetup(addon, atkValueSpan);

    // Forced, and this is not merely symmetry with the main window: the game
    // closes its addons while a quest is being turned in, so OnSetup runs again
    // the moment the player finishes the very thing the totals need to reflect.
    // Waiting out the throttle here shows a stale figure at precisely the
    // instant someone looks to see whether it counted.
    DataService.UpdateQuestData(true);

    // Set here rather than once at construction: the addon is reallocated every
    // time it opens, so a flag applied earlier would not survive.
    InternalAddon->IgnoreUIDisplayMode = Config.CompanionAlwaysVisible;

    // Scrolled rather than sized to fit. The content has no fixed height and no
    // useful upper bound — every allied society can cap at once — so a window
    // tall enough for the worst case would be mostly empty for everyone, and a
    // window sized to the current contents would change height as you played.
    // A fixed frame you can place once, with the overflow reachable, is the
    // behaviour that survives the next row being added.
    //
    // ContentNode properties are set before Size, as ScrollingNode requires.
    _scroll = new ScrollingNode<VerticalListNode>
    {
      Position = ContentStartPosition,
      IsVisible = true,
      AutoHideScrollBar = true
    };

    // FitContents makes the list measure only its *visible* children, which is
    // what makes hiding spare rows shrink the scroll range instead of leaving
    // empty space to scroll through.
    _scroll.ContentNode.ItemSpacing = ListSpacing;
    _scroll.ContentNode.FitContents = true;
    _scroll.Size = ContentSize;

    AddNode(_scroll);

    _swapButton = TitleBarButton.Gear(WindowNode, Size.X, "Full window (/tm)", () => OnSwapRequested());
    AddNode(_swapButton);

    for (int i = 0; i < MaxRows; i++)
    {
      HorizontalListNode container = new()
      {
        Size = new Vector2(LabelWidth + ValueWidth, RowHeight),
        IsVisible = true
      };

      TextNode label = MakeText(LabelWidth, AlignmentType.Left);
      TextNode value = MakeText(ValueWidth, AlignmentType.Right);

      container.AddNode(label);
      container.AddNode(value);

      _scroll.ContentNode.AddNode(container);
      _rows.Add((container, label, value));
    }

    // Every row is visible at this point, so the list currently measures its
    // full height. OnUpdate hides the spare ones and re-measures immediately
    // after; starting at the top means the clamp lands somewhere sensible.
    _scroll.ScrollToStart();
  }

  protected override void OnFinalize(AtkUnitBase* addon)
  {
    _rows.Clear();
    _scroll = null;
    _swapButton = null;

    base.OnFinalize(addon);
  }

  protected override void OnUpdate(AtkUnitBase* addon)
  {
    DataService.UpdateQuestData();

    int row = 0;

    SetFood(ref row);

    SetHeading(ref row, "Playtime");

    PlaytimeRecord? record = Playtime.Current;
    if (record is not null && record.LifetimePlaytime > TimeSpan.Zero)
    {
      TimeSpan total = record.LifetimePlaytime;
      SetRow(ref row, "Lifetime", $"{(int)total.TotalDays}d {total.Hours}h {total.Minutes}m");

      // The figure is exactly as old as the last /playtime, and this plugin
      // will not run that for you. Without the age it silently looks current.
      SetRow(ref row, "recorded",
        record.LifetimePlaytimeUpdatedUtc.HasValue
          ? Ago(DateTime.UtcNow - record.LifetimePlaytimeUpdatedUtc.Value)
          : "age unknown",
        muted: true);
    }
    else
    {
      SetRow(ref row, "Lifetime", "run /playtime", muted: true);
    }

    SetHeading(ref row, "Collectables");
    SetCollectables(ref row, "Gathered", Achievements.Gathered, AchievementSeries.Gathered);
    SetCollectables(ref row, "Crafted", Achievements.Crafted, AchievementSeries.Crafted);

    SetHeading(ref row, "Pacing");
    SetRow(ref row, $"This session ({Pacing.SessionQuests})", FormatPace(Pacing.SessionMinutesPerQuest));
    SetRow(ref row, "Overall", FormatPace(Pacing.OverallMinutesPerQuest));

    SetAlliedSociety(ref row);

    SetHeading(ref row, "Furthest behind  (lv.progress)");

    // Battle jobs only, and not the limited ones. This list answers "what should
    // I level next", and Blue Mage or Beastmaster cannot be levelled by any of
    // the means the rest of the list implies -- they would simply sit at the top
    // of it for ever. The flag comes from the ClassJob sheet, so a limited job
    // added in a future patch is excluded without anything being changed here.
    //
    // Ordered by level plus progress within it, so a job most of the way through
    // a level ranks above one that has just reached it -- comparing levels alone
    // put Miner and Botanist in the wrong order once.
    // Grouped by base class before ranking. Summoner and Scholar share
    // Arcanist's experience, so they are one level wearing two names — ranked
    // separately they filled two of the five slots with a single thing to go
    // and do, and moved in lockstep for ever after. Every other slot holds one
    // job, so nothing else is affected.
    //
    // Both names are kept rather than the class's: "Scholar/Summoner" says what
    // levelling it gets you, where "Arcanist" reads as a fourth thing to level.
    List<(string Name, float Progress)> jobs =
    [
      .. ProgressService.GetProgress()
        .Where((j) => j.IsUnlocked && j.Category == "combat" && !j.IsMaxLevel && !j.IsLimitedJob)
        .GroupBy((j) => j.ClassName ?? j.Name)
        .Select((slot) => (
          Name: string.Join("/", slot.Select((j) => j.Name)),
          Progress: slot.First().Level + slot.First().Fraction))
        .OrderBy((slot) => slot.Progress)
        .Take(LowestJobCount)
    ];

    if (jobs.Count == 0)
    {
      SetRow(ref row, "Every battle job is capped", "", muted: true);
    }
    else
    {
      // Level and progress as one number, the way the ledger stores them —
      // 68.230 is level 68, 23% of the way through. A fixed number of decimals
      // also right-aligns cleanly, which "68   23%" cannot.
      foreach ((string name, float progress) in jobs)
        SetRow(ref row, name,
          Math.Round(progress, 3, MidpointRounding.AwayFromZero).ToString("F3",
            CultureInfo.InvariantCulture));
    }

    // The container has to go, not just its text. FitContents measures visible
    // children, so a container left visible around two hidden labels still
    // claims its full height — which is exactly how the scroll range came to
    // describe thirty-four rows when seventeen were in use.
    for (; row < _rows.Count; row++)
    {
      (HorizontalListNode container, TextNode label, TextNode value) = _rows[row];
      container.IsVisible = label.IsVisible = value.IsVisible = false;
    }

    // The row count is genuinely variable — collectables appear once read,
    // societies appear as they cap or max, and the job list shortens as jobs
    // reach the ceiling. Re-measuring here is what keeps the scroll range
    // honest; without it the bar describes whatever the window last held.
    _scroll?.RecalculateSizes();
  }

  /// <summary>
  /// Allowances, and only the societies worth acting on.
  ///
  /// Listing all twenty would fill this window with zeroes, and listing every
  /// started one grows without limit. What actually changes a decision is: how
  /// many quests can still be picked up today, and which societies are at their
  /// point cap — because there, further dailies award nothing until an allied
  /// society main quest promotes you, and grinding them is wasted.
  ///
  /// The allowance is trustworthy except in one case, so it says so only in that
  /// case: held quests block the count from rolling over at reset until the last
  /// is handed in.
  /// </summary>
  private void SetAlliedSociety(ref int row)
  {
    SetHeading(ref row, "Allied society");

    int held = Societies.HeldQuests;

    SetRow(ref row, "Allowances", held > 0
      ? $"{Societies.Allowances}  ({held} held)"
      : $"{Societies.Allowances} / 12");

    foreach (SocietyStanding standing in Societies.GetStandings().Where((s) => s.IsCapped || s.IsTerminal))
      SetRow(ref row, standing.Name, standing.IsCapped ? "capped — main quest" : "maxed", muted: standing.IsTerminal);
  }

  /// <summary>
  /// First, because it is the only thing here that expires. Everything below is
  /// a standing figure that reads the same in ten minutes.
  ///
  /// The banked total is the point of the section rather than a footnote: the
  /// game hands out food constantly and almost nobody eats any of it, so the
  /// useful thing is not a recommendation but the discovery that you are
  /// carrying hours of unused bonus. It is a floor — see the service.
  /// </summary>
  private void SetFood(ref int row)
  {
    FoodReading reading = Food.Read();

    SetHeading(ref row, "Food");

    if (reading.WellFed)
    {
      TimeSpan left = TimeSpan.FromSeconds(reading.RemainingSeconds);

      SetRow(ref row, "Active food", Name(reading.Active));
      SetRow(ref row, "Time remaining", $"{(int)left.TotalMinutes}m {left.Seconds:00}s");

      // Zero is a real answer, and the one worth seeing: it means that was the
      // last of them and the next meal will have to be something else.
      if (reading.Active is not null)
        SetRow(ref row, "Still held", $"{reading.Active.Quantity}", muted: true);

      return;
    }

    if (reading.Best is null)
    {
      SetRow(ref row, "Recommended", "no food in bags", muted: true);
      return;
    }

    SetRow(ref row, "Recommended", Name(reading.Best));
    SetRow(ref row, "Held", $"{reading.Best.Quantity}", muted: true);
    SetRow(ref row, "In bags", Banked(reading.Banked), muted: true);
  }

  private static string Name(FoodChoice? food)
    => food is null ? "unknown" : food.HighQuality ? $"{food.Name} (HQ)" : food.Name;

  private static string Banked(TimeSpan banked)
    => banked >= TimeSpan.FromHours(1)
      ? $"{(int)banked.TotalHours}h {banked.Minutes}m of bonus"
      : $"{(int)banked.TotalMinutes}m of bonus";

  private void SetHeading(ref int row, string text)
  {
    if (row >= _rows.Count) return;

    (HorizontalListNode container, TextNode label, TextNode value) = _rows[row++];

    container.IsVisible = true;

    label.IsVisible = true;
    label.String = text;
    label.TextColor = Heading;

    value.IsVisible = false;
  }

  private void SetRow(ref int row, string name, string text, bool muted = false)
  {
    if (row >= _rows.Count) return;

    (HorizontalListNode container, TextNode label, TextNode value) = _rows[row++];

    container.IsVisible = true;
    label.IsVisible = value.IsVisible = true;

    label.String = name;
    value.String = text;

    label.TextColor = value.TextColor = muted ? Muted : Normal;
  }

  /// <summary>
  /// A collectable count, said as precisely as it can honestly be said.
  ///
  /// The game only reveals these when the Achievements window is open, so
  /// before that there is nothing to show and the row says so rather than
  /// showing a zero. When the tier that was looked at is already complete its
  /// number is that tier's requirement rather than a running total, so it is
  /// reported as a floor with a nudge toward a later tier.
  /// </summary>
  /// <summary>
  /// The hint names the achievement rather than saying "open Achievements",
  /// which was simply wrong: the window can be open, IsLoaded true, and still
  /// nothing read, because the client keeps the progress of only the last
  /// achievement fetched. Opening that one achievement's page is what works.
  /// </summary>
  private void SetCollectables(ref int row, string label, AchievementReading? reading, AchievementSeries series)
  {
    if (reading is null)
    {
      string source = Achievements.SourceFor(series);
      SetRow(ref row, label, source.Length > 0 ? $"see {source}" : "not read yet", muted: true);

      return;
    }

    // Age goes on the same line rather than its own. This window is small, and
    // two rows per figure pushed the job list off the bottom of it.
    string count = reading.IsExact ? $"{reading.Value:N0}" : $"{reading.Value:N0}+";
    SetRow(ref row, label, $"{count}   {Ago(DateTime.UtcNow - reading.TakenUtc)}");
  }

  private static string Ago(TimeSpan span)
  {
    if (span < TimeSpan.Zero || span.TotalMinutes < 1) return "just now";
    if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
    if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";

    return $"{(int)span.TotalDays}d ago";
  }

  /// <summary>Minutes per quest, in whichever unit reads without arithmetic.</summary>
  private static string FormatPace(double? minutes)
  {
    if (minutes is null or <= 0) return "—";
    if (minutes < 60) return $"{minutes.Value:F0}m / quest";

    return $"{(int)(minutes.Value / 60)}h {(int)(minutes.Value % 60)}m / quest";
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
