using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// One quest in the native browser's list: title on top, the details that
/// distinguish it underneath.
///
/// <see cref="ListNode{T,TU}"/> owns the pooling and scrolling, so this type
/// only has to describe a single row and fill it from a quest. Rows are reused
/// as the list scrolls, which is why <see cref="SetNodeData"/> must set every
/// field on every call rather than assuming an empty starting state.
/// </summary>
public class QuestListItemNode : ListItemWithFocusNav<Types.Quest>, IListItemNode
{
  public static float ItemHeight => 38.0f;

  /// <summary>
  /// Static because a list item is constructed by <see cref="ListNode{T,TU}"/>
  /// through a parameterless constructor, so there is nowhere to inject this.
  /// The browser sets it before handing the list any data.
  /// </summary>
  public static IQuestPatchService? PatchService { get; set; }
  public static IDataService? DataService { get; set; }
  public static IQuestMapService? MapService { get; set; }

  /// <summary>The map-flag icon, same id <c>AgentMap.SetFlagMapMarker</c> uses
  /// for a flag by default — so the button reads as the thing it places.</summary>
  private const uint FlagIconId = 0xEC91;

  /// <summary>The Trade Mentor status icon (OnlineStatus row 29), a hammer —
  /// the crafter and gatherer mark, for a button that copies crafting needs.</summary>
  private const uint CopyItemsIconId = 61543;

  private static readonly Vector4 Complete = new(0.55f, 0.55f, 0.55f, 1.0f);
  private static readonly Vector4 Incomplete = new(1.0f, 1.0f, 1.0f, 1.0f);
  private static readonly Vector4 Detail = new(0.6f, 0.6f, 0.6f, 1.0f);

  private TextNode TitleNode { get; }
  private TextNode DetailNode { get; }
  private RowIconButtonNode FlagButton { get; }
  private RowIconButtonNode CopyItemsButton { get; }

  private Types.Quest? _current;

  public QuestListItemNode()
  {
    TitleNode = new TextNode { TextFlags = TextFlags.Ellipsis | TextFlags.Emboss, FontSize = 12 };
    TitleNode.AttachNode(this);

    DetailNode = new TextNode { TextFlags = TextFlags.Ellipsis, FontSize = 11, TextColor = Detail };
    DetailNode.AttachNode(this);

    FlagButton = new RowIconButtonNode
    {
      Size = new Vector2(22.0f, 22.0f),
      IconId = FlagIconId,
      IsVisible = false,
      OnClick = () => { if (_current is not null) MapService?.OpenIssuerOnMap(_current); }
    };
    FlagButton.AttachNode(this);

    CopyItemsButton = new RowIconButtonNode
    {
      Size = new Vector2(22.0f, 22.0f),
      IconId = CopyItemsIconId,
      IsVisible = false,
      OnClick = CopyRequiredItems
    };
    CopyItemsButton.AttachNode(this);
  }

  /// <summary>
  /// Puts what one turn-in needs on the clipboard, in the "1x Item Name" lines
  /// Teamcraft and Allagan Tools import. The tooltip already shows the exact
  /// text, so a copy needs no message of its own. Button callbacks run on the
  /// framework thread, which is the thread ImGui renders on, so the clipboard
  /// call is safe from here.
  /// </summary>
  private void CopyRequiredItems()
  {
    if (_current is not { RequiredItems.Count: > 0 }) return;
    ImGui.SetClipboardText(string.Join("\n", _current.RequiredItems));
  }

  /// <summary>
  /// Called by the list for every visible row when the panel refreshes. The
  /// row's quest has not changed, but whether it is complete, held, or ready
  /// to turn in can have, so only the title is redrawn.
  /// </summary>
  public override void Update() => RefreshTitle();

  /// <summary>
  /// Title text and colour, the parts of a row that follow the game's state.
  /// Leve state shows as prefixes: ">" for a held leve, a check for one whose
  /// items are already in the bags. Text, not colour, so it reads the same to
  /// everyone and survives the completed-grey title.
  /// </summary>
  private void RefreshTitle()
  {
    if (_current is null) return;

    bool complete = DataService?.IsQuestComplete(_current) ?? false;

    string prefix = "";
    if (DataService?.IsLeveAccepted(_current) ?? false) prefix += "> ";
    if (DataService?.HasLeveItems(_current) ?? false) prefix += "✓ ";

    TitleNode.String = prefix + _current.Title;
    TitleNode.TextColor = complete ? Complete : Incomplete;
  }

  protected override void SetNodeData(Types.Quest itemData)
  {
    _current = itemData;

    RefreshTitle();

    // Level and area place the quest; the patch says when it arrived, which is
    // the one fact the game's own journal cannot tell you.
    List<string> parts = [$"Lv {itemData.Level}"];

    // A leve's journal section names its type, not who can take it, and only
    // the levemete's own window says whether it takes repeat hand-ins.
    if (itemData.Job is { Length: > 0 }) parts.Add(itemData.Job);
    if (itemData.TurnIns > 1) parts.Add($"Up to {itemData.TurnIns} turn-ins");

    if (itemData.Area.Length > 0) parts.Add(itemData.Area);

    string? patch = PatchService?.GetPatch(itemData.Ids);
    if (patch is not null) parts.Add($"Patch {patch}");

    DetailNode.String = string.Join("  •  ", parts);

    string? label = MapService?.GetIssuerLabel(itemData);
    FlagButton.IsVisible = label is not null;
    FlagButton.TextTooltip = label ?? "";

    CopyItemsButton.IsVisible = itemData.RequiredItems.Count > 0;
    CopyItemsButton.TextTooltip = CopyItemsButton.IsVisible
      ? $"Copy for Teamcraft or Allagan Tools:\n{string.Join("\n", itemData.RequiredItems)}"
      : "";

    // Rows are pooled, so which buttons show changes as the list scrolls.
    Layout();
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();
    Layout();
  }

  /// <summary>
  /// Buttons stack in from the right edge, flag outermost. Only visible ones
  /// take space, so an ordinary quest's title keeps the full width.
  /// </summary>
  private void Layout()
  {
    float right = Width - 8.0f;
    float buttonY = (Height - FlagButton.Height) / 2.0f;

    foreach (RowIconButtonNode button in new[] { FlagButton, CopyItemsButton })
    {
      if (!button.IsVisible) continue;
      right -= button.Width;
      button.Position = new Vector2(right, buttonY);
      right -= 4.0f;
    }

    float textWidth = right - 10.0f;

    TitleNode.Size = new Vector2(textWidth, 20.0f);
    TitleNode.Position = new Vector2(10.0f, 2.0f);

    DetailNode.Size = new Vector2(textWidth, 16.0f);
    DetailNode.Position = new Vector2(10.0f, 21.0f);
  }
}
