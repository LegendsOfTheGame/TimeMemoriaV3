using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// Fed status and what to do about it, plus every food actually in the bags
/// and its bonuses — which the companion window's single recommendation row
/// does not cover.
/// </summary>
public class BonusesPanelNode : TabPanelNode
{
  private const float RowHeight = 20.0f;
  private const float NameWidth = 190.0f;
  private const float StatsWidth = 600.0f;

  /// <summary>
  /// Summary rows plus headroom for the held-food list, which grows with how
  /// many distinct meals are in the bags — doubled wherever a food is held in
  /// both qualities, since NQ and HQ draw as separate rows. NewsPanelNode's own
  /// pool sits at 48 for similarly variable content; this follows the same
  /// margin with room for the extra split.
  /// </summary>
  private const int MaxRows = 80;

  private static readonly Vector4 Heading = new(0.6f, 0.8f, 1.0f, 1.0f);
  private static readonly Vector4 Normal = new(1.0f, 1.0f, 1.0f, 1.0f);
  private static readonly Vector4 Muted = new(0.6f, 0.6f, 0.6f, 1.0f);
  private static readonly Vector4 HighQuality = new(0.45f, 0.85f, 0.45f, 1.0f);

  public required IFoodService Food { get; init; }

  private readonly ScrollingNode<VerticalListNode> _scroll;
  private readonly List<(HorizontalListNode Container, TextNode Label, TextNode Value)> _rows = [];

  /// <summary>Rows drawn last time, so the scroll range is only re-measured when it moved.</summary>
  private int _lastRowCount = -1;

  public BonusesPanelNode()
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
        Size = new Vector2(NameWidth + StatsWidth, RowHeight),
        IsVisible = true
      };

      TextNode label = MakeText(NameWidth, AlignmentType.Left);
      TextNode value = MakeText(StatsWidth, AlignmentType.Left);

      container.AddNode(label);
      container.AddNode(value);

      _scroll.ContentNode.AddNode(container);
      _rows.Add((container, label, value));
    }

    _scroll.ScrollToStart();
  }

  public override void Refresh()
  {
    FoodReading reading = Food.Read();

    int row = 0;

    SetHeading(ref row, "Food");

    if (reading.WellFed)
    {
      TimeSpan left = TimeSpan.FromSeconds(reading.RemainingSeconds);

      Set(ref row, "Active food", Name(reading.Active));
      Set(ref row, "Time remaining", $"{(int)left.TotalMinutes}m {left.Seconds:00}s");

      // Zero is a real answer, and the one worth seeing: it means that was the
      // last of them and the next meal will have to be something else.
      if (reading.Active is not null)
        Set(ref row, "Still held", $"{reading.Active.Quantity}", muted: true);
    }
    else if (reading.Best is null)
    {
      Set(ref row, "Recommended", "no food in bags", muted: true);
    }
    else
    {
      Set(ref row, "Recommended", Name(reading.Best));
      Set(ref row, "Held", $"{reading.Best.Quantity}", muted: true);
      Set(ref row, "In bags", Banked(reading.Banked), muted: true);
    }

    SetHeading(ref row, $"In your bags ({reading.Held.Count})");

    if (reading.Held.Count == 0)
    {
      Set(ref row, "", "nothing found in Inventory1-4 or SaddleBag1-2", muted: true);
    }
    else
    {
      // FoodService already sorts Held by pursuit then by score, so grouping
      // here just draws the boundaries that ordering implies — highest-ranked
      // food in each pursuit first.
      foreach (var group in reading.Held.GroupBy((stack) => stack.Pursuit))
      {
        SetHeading(ref row, $"{PursuitName(group.Key)} ({group.Count()})");

        foreach (FoodStack stack in group)
          Set(ref row, $"{stack.Quantity}x {stack.Name}", stack.Stats, highQuality: stack.HighQuality);
      }
    }

    int used = row;

    for (; row < _rows.Count; row++)
    {
      (HorizontalListNode container, TextNode label, TextNode value) = _rows[row];
      container.IsVisible = label.IsVisible = value.IsVisible = false;
    }

    // Guarded on the count, since Refresh runs every frame and re-measuring
    // walks the whole pool. The scroll range only becomes wrong when the
    // number of distinct held foods changes.
    if (used != _lastRowCount)
    {
      _lastRowCount = used;
      _scroll.RecalculateSizes();
    }
  }

  private static string PursuitName(FoodPursuit pursuit) => pursuit switch
  {
    FoodPursuit.Combat => "Combat food",
    FoodPursuit.Crafting => "Crafting food",
    FoodPursuit.Gathering => "Gathering food",
    _ => pursuit.ToString()
  };

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

  private void Set(ref int row, string name, string text, bool muted = false, bool highQuality = false)
  {
    if (row >= _rows.Count) return;

    (HorizontalListNode container, TextNode label, TextNode value) = _rows[row++];

    container.IsVisible = label.IsVisible = value.IsVisible = true;
    label.String = name;
    value.String = text;

    Vector4 colour = muted ? Muted : Normal;

    // Only the name marks quality — the "(HQ)" suffix is already in the text
    // too, so colouring both columns would just repeat it.
    label.TextColor = highQuality ? HighQuality : colour;
    value.TextColor = colour;
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

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
    TextFlags = TextFlags.Ellipsis,
    IsVisible = true
  };
}
