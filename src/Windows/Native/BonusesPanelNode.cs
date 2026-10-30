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

  /// <summary>
  /// The game's own role icons, from its glyph font (Dalamud's BitmapFontIcon;
  /// HaselDebug lists them under Gaiji font data). They draw inline in the text,
  /// the way the game shows roles in its own windows. These replaced coloured
  /// squares whose colours were our own picks, not the game's.
  /// </summary>
  private static readonly Dictionary<string, uint> RoleIcons = new()
  {
    ["Tank"] = 82,
    ["Healer"] = 83,
    ["DPS"] = 84,
    ["Crafter"] = 85,
    ["Gatherer"] = 86
  };

  public required IFoodService Food { get; init; }

  private readonly ScrollingNode<VerticalListNode> _scroll;
  private readonly List<(HorizontalListNode Container, TextNode Label, TextNode Value)> _rows = [];

  /// <summary>Rows drawn last time, so the scroll range is only re-measured when it moved.</summary>
  private int _lastRowCount = -1;

  /// <summary>Whether a heading has been drawn yet this refresh: the first one gets no gap.</summary>
  private bool _headingDrawn;

  /// <summary>Set when a row changed height this refresh, so the list is laid out again.</summary>
  private bool _relayout;

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
    _headingDrawn = false;
    _relayout = false;

    SetHeading(ref row, "Food");

    if (reading.WellFed)
    {
      TimeSpan left = TimeSpan.FromSeconds(reading.RemainingSeconds);

      Set(ref row, "Active food", Name(reading.Active));
      Set(ref row, "Time remaining", $"{(int)left.TotalMinutes}m {left.Seconds:00}s");

      // Zero is a real answer, and the one worth seeing: it means that was the
      // last of them and the next meal will have to be something else.
      if (reading.Active is not null)
        Set(ref row, "Still held", $"{reading.Active.Quantity}");
    }
    else if (reading.Best is null)
    {
      Set(ref row, "Recommended", "no food in bags");
    }
    else
    {
      Set(ref row, "Recommended", Name(reading.Best));
      Set(ref row, "Held", $"{reading.Best.Quantity}");
      Set(ref row, "In bags", Banked(reading.Banked));
    }

    SetHeading(ref row, $"In your bags ({reading.Held.Count})");

    if (reading.Held.Count == 0)
    {
      Set(ref row, "", "nothing found in your bags or saddlebag");
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
          Set(ref row, $"{stack.Quantity}x {stack.Name}", stack.Stats,
            role: stack.Role.Length == 0 ? null : stack.Role, tooltip: stack.Where);
      }
    }

    int used = row;

    for (; row < _rows.Count; row++)
    {
      (HorizontalListNode container, TextNode label, TextNode value) = _rows[row];
      container.IsVisible = label.IsVisible = value.IsVisible = false;
      SetTooltip(row, label, "");
    }

    // Guarded, since Refresh runs every frame and re-measuring walks the whole
    // pool. The layout only goes wrong when the number of rows changes, or a
    // pooled row turns into a heading (taller, for its gap) or back.
    if (used != _lastRowCount || _relayout)
    {
      _lastRowCount = used;
      _scroll.ContentNode.RecalculateLayout();
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

    SetTooltip(row, _rows[row].Label, "");
    (HorizontalListNode container, TextNode label, TextNode value) = _rows[row++];

    container.IsVisible = true;
    label.IsVisible = true;
    label.String = text;
    value.IsVisible = false;

    _relayout |= NativeTheme.ShapeRow(container, label, RowHeight, heading: true, first: !_headingDrawn);
    _headingDrawn = true;
  }

  /// <summary>
  /// One row in the theme's text colour. A food row starts its stats with the
  /// game's role icon and the role in words ("[icon] Tank · Tenacity +10%..."),
  /// so the role never rests on colour. Quality needs nothing extra: "(HQ)" is
  /// already in the name.
  /// </summary>
  private void Set(ref int row, string name, string text, string? role = null, string tooltip = "")
  {
    if (row >= _rows.Count) return;

    SetTooltip(row, _rows[row].Label, tooltip);
    (HorizontalListNode container, TextNode label, TextNode value) = _rows[row++];

    container.IsVisible = label.IsVisible = value.IsVisible = true;
    label.String = name;
    value.String = role is not null && RoleIcons.TryGetValue(role, out uint icon)
      ? new Lumina.Text.SeStringBuilder().AppendIcon(icon).Append($" {role} · {text}").ToReadOnlySeString()
      : role is null ? text : $"{role} · {text}";
    value.TextColor = NativeTheme.Text;

    _relayout |= NativeTheme.ShapeRow(container, label, RowHeight, heading: false, first: false);
  }

  /// <summary>Tooltip text each row last had. Rows are reused, so it follows the row.</summary>
  private readonly string[] _tooltips = new string[MaxRows];

  /// <summary>
  /// Where a food is held, on hovering its name. Only set when it changes:
  /// Refresh runs every frame, and each change makes the addon rebuild its
  /// collision list.
  /// </summary>
  private void SetTooltip(int row, TextNode label, string text)
  {
    if ((_tooltips[row] ?? "") == text) return;

    _tooltips[row] = text;
    label.TextTooltip = text;
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
    TextFlags = TextFlags.Ellipsis,
    IsVisible = true
  };
}
