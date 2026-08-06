using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// One quest that has appeared in the game since this plugin started watching.
/// </summary>
public class NewQuestListItemNode : ListItemWithFocusNav<NewQuest>, IListItemNode
{
  public static float ItemHeight => 38.0f;

  /// <summary>Set by the panel; the list builds rows through a parameterless constructor.</summary>
  public static IQuestPatchService? PatchService { get; set; }

  private static readonly Vector4 Complete = new(0.55f, 0.55f, 0.55f, 1.0f);
  private static readonly Vector4 Incomplete = new(1.0f, 1.0f, 1.0f, 1.0f);
  private static readonly Vector4 Detail = new(0.6f, 0.6f, 0.6f, 1.0f);

  private TextNode TitleNode { get; }
  private TextNode DetailNode { get; }

  public NewQuestListItemNode()
  {
    TitleNode = new TextNode { TextFlags = TextFlags.Ellipsis | TextFlags.Emboss, FontSize = 12 };
    TitleNode.AttachNode(this);

    DetailNode = new TextNode { TextFlags = TextFlags.Ellipsis, FontSize = 11, TextColor = Detail };
    DetailNode.AttachNode(this);
  }

  protected override void SetNodeData(NewQuest itemData)
  {
    bool complete = QuestManager.IsQuestComplete(itemData.Id);

    TitleNode.String = itemData.Title;
    TitleNode.TextColor = complete ? Complete : Incomplete;

    // The patch beside the date is what separates "new to the game" from "new
    // to this plugin" — an old patch with a recent date is the latter.
    string patch = PatchService?.GetPatch([itemData.Id]) is { } p ? $"Patch {p}" : "patch unknown";

    DetailNode.String = $"Lv {itemData.Level}  •  {itemData.Expansion} › {itemData.Section}  •  {patch}  •  seen {itemData.SeenOn}";
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

/// <summary>
/// Quests added to the game since the plugin's baseline snapshot was taken.
/// </summary>
public class WhatsNewPanelNode : TabPanelNode
{
  private const float HeadingHeight = 22.0f;

  public required IQuestSnapshotService Snapshot { get; init; }
  public required IQuestPatchService PatchService { get; init; }

  private readonly TextNode _heading;
  private readonly ListNode<NewQuest, NewQuestListItemNode> _list;

  public WhatsNewPanelNode()
  {
    _heading = new TextNode
    {
      TextFlags = TextFlags.Ellipsis,
      FontSize = 12,
      TextColor = new Vector4(0.6f, 0.8f, 1.0f, 1.0f),
      IsVisible = true
    };
    _heading.AttachNode(this);

    _list = new ListNode<NewQuest, NewQuestListItemNode>
    {
      IsVisible = true,
      OptionsList = [],
      ShowNoResultsPlaceholder = false
    };
    _list.AttachNode(this);
  }

  public override void OnShown()
  {
    NewQuestListItemNode.PatchService = PatchService;
    Refresh();
  }

  public override void Refresh()
  {
    IReadOnlyList<NewQuest> additions = Snapshot.Additions;

    _heading.String = additions.Count == 0
      ? $"Nothing new since {Snapshot.BaselineDate} — baseline {Snapshot.KnownQuests} quests, build {Snapshot.GameVersion}"
      : $"{additions.Count} quest{(additions.Count == 1 ? "" : "s")} added since {Snapshot.BaselineDate}";

    if (_list.OptionsList.Count != additions.Count) _list.OptionsList = [.. additions];
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    _heading.Size = new Vector2(Width, HeadingHeight);
    _heading.Position = new Vector2(4.0f, 0.0f);

    _list.Size = new Vector2(Width, Height - HeadingHeight);
    _list.Position = new Vector2(0.0f, HeadingHeight);
  }
}
