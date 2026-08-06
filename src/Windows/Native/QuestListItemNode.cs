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
  public static float ItemHeight => 40.0f;

  /// <summary>
  /// Static because a list item is constructed by <see cref="ListNode{T,TU}"/>
  /// through a parameterless constructor, so there is nowhere to inject this.
  /// The browser sets it before handing the list any data.
  /// </summary>
  public static IQuestPatchService? PatchService { get; set; }
  public static IDataService? DataService { get; set; }

  private static readonly Vector4 Complete = new(0.55f, 0.55f, 0.55f, 1.0f);
  private static readonly Vector4 Incomplete = new(1.0f, 1.0f, 1.0f, 1.0f);
  private static readonly Vector4 Detail = new(0.6f, 0.6f, 0.6f, 1.0f);

  private TextNode TitleNode { get; }
  private TextNode DetailNode { get; }

  public QuestListItemNode()
  {
    TitleNode = new TextNode { TextFlags = TextFlags.Ellipsis | TextFlags.Emboss, FontSize = 12 };
    TitleNode.AttachNode(this);

    DetailNode = new TextNode { TextFlags = TextFlags.Ellipsis, FontSize = 11, TextColor = Detail };
    DetailNode.AttachNode(this);
  }

  protected override void SetNodeData(Types.Quest itemData)
  {
    bool complete = DataService?.IsQuestComplete(itemData) ?? false;

    TitleNode.String = itemData.Title;
    TitleNode.TextColor = complete ? Complete : Incomplete;

    // Level and area place the quest; the patch says when it arrived, which is
    // the one fact the game's own journal cannot tell you.
    List<string> parts = [$"Lv {itemData.Level}"];

    if (itemData.Area.Length > 0) parts.Add(itemData.Area);

    string? patch = PatchService?.GetPatch(itemData.Ids);
    if (patch is not null) parts.Add($"Patch {patch}");

    DetailNode.String = string.Join("  •  ", parts);
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    TitleNode.Size = new Vector2(Width - 12.0f, 20.0f);
    TitleNode.Position = new Vector2(10.0f, 2.0f);

    DetailNode.Size = new Vector2(Width - 12.0f, 16.0f);
    DetailNode.Position = new Vector2(10.0f, 21.0f);
  }
}
