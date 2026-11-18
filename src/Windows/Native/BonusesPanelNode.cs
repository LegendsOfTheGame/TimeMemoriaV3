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
  private const float StatsWidth = 552.0f;

  /// <summary>
  /// The Eat button's slot at the end of each row. A fixed-width ResNode that
  /// stays visible on every food row, so hiding the button inside it does not
  /// shift the columns (HorizontalListNode skips hidden nodes).
  /// </summary>
  private const float EatWidth = 48.0f;

  /// <summary>
  /// Where the food is ("B1, B4, S"), after the Eat button. A column of its
  /// own: in the name the mark was cut off by the ellipsis on long names.
  /// </summary>
  private const float LocationWidth = 90.0f;

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

  /// <summary>Per row: the Eat slot, its button, and the meal it eats (null: no button on this row).</summary>
  private readonly List<(ResNode Slot, TextButtonNode Button)> _eat = [];
  private readonly FoodStack?[] _eatTargets = new FoodStack?[MaxRows];

  /// <summary>Per row: the location column.</summary>
  private readonly List<TextNode> _location = [];

  /// <summary>Last state written to each button, so nodes are only touched when it changes.</summary>
  private readonly (bool Visible, bool Enabled)[] _eatState = new (bool, bool)[MaxRows];

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
        Size = new Vector2(NameWidth + StatsWidth + EatWidth + LocationWidth, RowHeight),
        IsVisible = true
      };

      TextNode label = MakeText(NameWidth, AlignmentType.Left);
      TextNode value = MakeText(StatsWidth, AlignmentType.Left);

      ResNode slot = new() { Size = new Vector2(EatWidth, RowHeight), IsVisible = true };

      int index = i;
      TextButtonNode button = new()
      {
        Size = new Vector2(EatWidth, RowHeight),
        String = "Eat",
        TextTooltip = "Eat one of this meal. One click, one item.",
        IsVisible = false,
        OnClick = () => EatRow(index)
      };
      button.AttachNode(slot);

      container.AddNode(label);
      container.AddNode(value);
      container.AddNode(slot);

      TextNode location = MakeText(LocationWidth, AlignmentType.Left);
      location.TextTooltip = "B1 to B4: the inventory block, as /isearch numbers it. S: the saddlebag.";
      container.AddNode(location);

      _scroll.ContentNode.AddNode(container);
      _rows.Add((container, label, value));
      _eat.Add((slot, button));
      _location.Add(location);
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

        // Every row in the bags gets an Eat button: the player reads the whole
        // list and picks, so the plugin decides nothing (PAC, 7 Nov 2026).
        foreach (FoodStack stack in group)
          Set(ref row, $"{stack.Quantity}x {stack.Name}", stack.Stats,
            role: stack.Role.Length == 0 ? null : stack.Role, tooltip: stack.Where, eat: stack);
      }
    }

    int used = row;

    for (; row < _rows.Count; row++)
    {
      (HorizontalListNode container, TextNode label, TextNode value) = _rows[row];
      container.IsVisible = label.IsVisible = value.IsVisible = false;
      SetTooltip(row, label, "");
      SetEat(row, null);
      SetLocation(row, "");
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
    _eat[row - 1].Slot.IsVisible = false;
    SetEat(row - 1, null);
    SetLocation(row - 1, "");

    _relayout |= NativeTheme.ShapeRow(container, label, RowHeight, heading: true, first: !_headingDrawn);
    _headingDrawn = true;
  }

  /// <summary>
  /// One row in the theme's text colour. A food row starts its stats with the
  /// game's role icon and the role in words ("[icon] Tank · Tenacity +10%..."),
  /// so the role never rests on colour. Quality needs nothing extra: "(HQ)" is
  /// already in the name.
  /// </summary>
  private void Set(ref int row, string name, string text, string? role = null, string tooltip = "",
    FoodStack? eat = null)
  {
    if (row >= _rows.Count) return;

    SetTooltip(row, _rows[row].Label, tooltip);
    _eat[row].Slot.IsVisible = true;
    SetEat(row, eat is { InBags: true } ? eat : null);
    SetLocation(row, eat?.Location ?? "");
    (HorizontalListNode container, TextNode label, TextNode value) = _rows[row++];

    container.IsVisible = label.IsVisible = value.IsVisible = true;
    label.String = name;
    value.String = role is not null && RoleIcons.TryGetValue(role, out uint icon)
      ? new Lumina.Text.SeStringBuilder().AppendIcon(icon).Append($" {role} · {text}").ToReadOnlySeString()
      : role is null ? text : $"{role} · {text}";
    value.TextColor = NativeTheme.Text;

    _relayout |= NativeTheme.ShapeRow(container, label, RowHeight, heading: false, first: false);
  }

  /// <summary>
  /// Shows the row's Eat button for a meal in the bags, enabled only when the
  /// game would accept it now. Null hides it. Refresh runs every frame, so the
  /// nodes are only written when the state changes.
  /// </summary>
  private void SetEat(int row, FoodStack? stack)
  {
    _eatTargets[row] = stack;

    bool visible = stack is not null;
    bool enabled = visible && Food.CanEat(stack!.ItemId, stack.HighQuality);
    if (_eatState[row] == (visible, enabled)) return;

    _eatState[row] = (visible, enabled);
    TextButtonNode button = _eat[row].Button;
    button.IsVisible = visible;
    button.IsEnabled = enabled;
  }

  /// <summary>Location text each row last had, so the node is only written when it changes.</summary>
  private readonly string[] _locations = new string[MaxRows];

  /// <summary>The location column. Empty text leaves the column in place, so the rows stay aligned.</summary>
  private void SetLocation(int row, string text)
  {
    if ((_locations[row] ?? "") == text) return;

    _locations[row] = text;
    // Two spaces keep the text off the Eat button's edge.
    _location[row].String = text.Length > 0 ? $"  {text}" : "";
    _location[row].TextColor = NativeTheme.Text;
  }

  /// <summary>One click, one item. Nothing repeats.</summary>
  private void EatRow(int row)
  {
    if (_eatTargets[row] is { } stack) Food.Eat(stack.ItemId, stack.HighQuality);
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
