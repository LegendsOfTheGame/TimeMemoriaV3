using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// The Overview tab as a native game window.
///
/// Every row here has the same shape — a label, a count, a percentage — so one
/// pooled row type covers headings and data alike. A heading is simply a row
/// with its count and percentage hidden. That keeps <see cref="OnSetup"/> to a
/// single loop and means <see cref="OnUpdate"/> never has to add or remove a
/// node, only change text and visibility.
/// </summary>
public unsafe class OverviewAddon : NativeAddon
{
  private const float RowHeight = 20.0f;
  private const float LabelWidth = 210.0f;
  private const float CountWidth = 110.0f;
  private const float PercentWidth = 55.0f;

  /// <summary>
  /// Fixed pool, not derived from live state. OnSetup runs once, and if it ran
  /// while no character was loaded a state-derived count would be zero and the
  /// window would stay empty for the rest of the session.
  /// </summary>
  private const int MaxRows = 28;

  private static readonly Vector4 Heading = new(0.6f, 0.8f, 1.0f, 1.0f);
  private static readonly Vector4 Normal = new(1.0f, 1.0f, 1.0f, 1.0f);
  private static readonly Vector4 Dimmed = new(0.6f, 0.6f, 0.6f, 1.0f);

  public required IDataService DataService { get; init; }

  private VerticalListNode? _list;
  private readonly List<(TextNode Label, TextNode Count, TextNode Percent)> _rows = [];

  protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
  {
    _list = new VerticalListNode
    {
      Position = ContentStartPosition,
      Size = ContentSize,
      ItemSpacing = 1.0f,
      IsVisible = true
    };

    AddNode(_list);

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

      _list.AddNode(container);
      _rows.Add((label, count, percent));
    }
  }

  protected override void OnUpdate(AtkUnitBase* addon)
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
      // silently disappears -- it is visibly present and visibly not counted.
      foreach (CategoryProgress category in categories)
        SetRow(ref row, category.Name, category.NumComplete, category.Total, category.Excluded);
    }

    // Anything the data did not fill this pass.
    for (; row < _rows.Count; row++)
      Hide(_rows[row]);
  }

  private void SetHeading(ref int row, string text)
  {
    if (row >= _rows.Count) return;

    (TextNode label, TextNode count, TextNode percent) = _rows[row++];

    label.IsVisible = true;
    label.String = text;
    label.TextColor = Heading;

    count.IsVisible = percent.IsVisible = false;
  }

  private void SetRow(ref int row, string name, int complete, int total, bool dimmed = false)
  {
    if (row >= _rows.Count) return;

    (TextNode label, TextNode count, TextNode percent) = _rows[row++];

    label.IsVisible = count.IsVisible = percent.IsVisible = true;

    label.String = name;
    count.String = $"{complete}/{total}";
    percent.String = total > 0 ? $"{(int)(complete / (float)total * 100f)}%" : "—";

    Vector4 colour = dimmed ? Dimmed : Normal;
    label.TextColor = count.TextColor = percent.TextColor = colour;
  }

  private static void Hide((TextNode Label, TextNode Count, TextNode Percent) row)
    => row.Label.IsVisible = row.Count.IsVisible = row.Percent.IsVisible = false;

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
