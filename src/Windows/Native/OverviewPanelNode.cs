using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// Quest completion by expansion and by category.
///
/// Every line is label / count / percent, so one pooled row type serves
/// headings and data alike — a heading is a row with its count and percent
/// hidden. Rows are built once and only ever have their text and visibility
/// changed, which is what keeps a retained tree cheap.
/// </summary>
public class OverviewPanelNode : TabPanelNode
{
  private const float RowHeight = 20.0f;
  private const float LabelWidth = 230.0f;
  private const float CountWidth = 110.0f;
  private const float PercentWidth = 55.0f;

  /// <summary>
  /// Fixed pool rather than one derived from live data, which would be zero if
  /// this were built before a character loaded.
  /// </summary>
  private const int MaxRows = 32;

  private static readonly Vector4 Heading = new(0.6f, 0.8f, 1.0f, 1.0f);
  private static readonly Vector4 Normal = new(1.0f, 1.0f, 1.0f, 1.0f);
  private static readonly Vector4 Dimmed = new(0.6f, 0.6f, 0.6f, 1.0f);

  public required IDataService DataService { get; init; }
  public required IAchievementService Achievements { get; init; }

  // Scrolls because the rows outgrew the window: with every category and the
  // achievements line showing, the last rows were cut off at the bottom edge.
  private readonly ScrollingNode<VerticalListNode> _scroll;
  private readonly List<(HorizontalListNode Container, TextNode Label, TextNode Count, TextNode Percent)> _rows = [];

  /// <summary>Rows in use last time the scroll range was measured.</summary>
  private int _lastRowCount = -1;

  public OverviewPanelNode()
  {
    // ContentNode properties are set before Size, as ScrollingNode requires.
    _scroll = new ScrollingNode<VerticalListNode> { IsVisible = true, AutoHideScrollBar = true };
    _scroll.ContentNode.ItemSpacing = 1.0f;
    _scroll.ContentNode.FitContents = true;
    _scroll.AttachNode(this);

    for (int i = 0; i < MaxRows; i++)
    {
      HorizontalListNode container = new()
      {
        Size = new Vector2(LabelWidth + CountWidth + PercentWidth, RowHeight),
        IsVisible = true
      };

      TextNode label = MakeText(LabelWidth, AlignmentType.Left);
      TextNode count = MakeText(CountWidth, AlignmentType.Right);
      TextNode percent = MakeText(PercentWidth, AlignmentType.Right);

      container.AddNode(label);
      container.AddNode(count);
      container.AddNode(percent);

      _scroll.ContentNode.AddNode(container);
      _rows.Add((container, label, count, percent));
    }

    _scroll.ScrollToStart();
  }

  public override void Refresh()
  {
    IReadOnlyList<ExpansionProgress> expansions = DataService.ExpansionProgress;
    IReadOnlyList<CategoryProgress> categories = DataService.CategoryProgress;

    int row = 0;

    SetHeading(ref row, "Quest Completion Progress");

    // Expansion figures already account for whatever the settings exclude, so
    // these rows sum to the overall line rather than merely resembling it.
    SetRow(ref row, "Overall", expansions.Sum((e) => e.NumComplete), expansions.Sum((e) => e.Total));

    foreach (ExpansionProgress expansion in expansions)
      SetRow(ref row, expansion.Name, expansion.NumComplete, expansion.Total);

    if (categories.Count > 0)
    {
      SetHeading(ref row, "By Category");

      // An excluded section still shows its real numbers, dimmed, so nothing
      // silently disappears — it is visibly present and visibly not counted.
      foreach (CategoryProgress category in categories)
        SetRow(ref row, category.Name, category.NumComplete, category.Total, category.Excluded);
    }

    // Absent, not zero, until the Achievements window has loaded this login --
    // the same distinction "By Category" draws by hiding when there is nothing
    // to show, rather than printing a heading over a false 0/0.
    if (Achievements.Totals is { } totals)
    {
      SetHeading(ref row, "Achievements");
      SetRow(ref row, "Overall", totals.Complete, totals.Total);
    }

    // One empty row after the last item, so it does not sit on the border.
    SetHeading(ref row, "");
    int used = row;

    // The container is hidden with its text: a hidden label in a visible
    // container still claims its height, and the scroll range would then
    // describe every pooled row rather than the ones in use.
    for (; row < _rows.Count; row++)
    {
      (HorizontalListNode container, TextNode label, TextNode count, TextNode percent) = _rows[row];
      container.IsVisible = label.IsVisible = count.IsVisible = percent.IsVisible = false;
    }

    // Guarded on the count, since Refresh runs every frame and re-measuring
    // walks the whole pool.
    if (used != _lastRowCount)
    {
      _lastRowCount = used;
      _scroll.RecalculateSizes();
    }
  }

  private void SetHeading(ref int row, string text)
  {
    if (row >= _rows.Count) return;

    (HorizontalListNode container, TextNode label, TextNode count, TextNode percent) = _rows[row++];

    container.IsVisible = label.IsVisible = true;
    label.String = text;
    label.TextColor = Heading;

    count.IsVisible = percent.IsVisible = false;
  }

  private void SetRow(ref int row, string name, int complete, int total, bool dimmed = false)
  {
    if (row >= _rows.Count) return;

    (HorizontalListNode container, TextNode label, TextNode count, TextNode percent) = _rows[row++];

    container.IsVisible = label.IsVisible = count.IsVisible = percent.IsVisible = true;

    label.String = name;
    count.String = $"{complete}/{total}";
    percent.String = total > 0 ? $"{(int)(complete / (float)total * 100f)}%" : "—";

    Vector4 colour = dimmed ? Dimmed : Normal;
    label.TextColor = count.TextColor = percent.TextColor = colour;
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    _scroll.Size = new Vector2(Width, Height);
    _scroll.Position = new Vector2(0.0f, 0.0f);
    _scroll.RecalculateSizes();
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
