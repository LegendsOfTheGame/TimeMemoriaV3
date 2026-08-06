using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// The right-hand pane of the quest browser: everything known about whichever
/// quest is selected, or an invitation to select one.
///
/// Fields are built once and rewritten on selection. Nothing here is created or
/// destroyed as the selection changes, which is the whole point of a retained
/// tree — the alternative would rebuild a dozen nodes on every click.
/// </summary>
public class QuestInfoNode : SimpleComponentNode
{
  private const float LineHeight = 20.0f;
  private const float Margin = 12.0f;

  private static readonly Vector4 Heading = new(0.6f, 0.8f, 1.0f, 1.0f);
  private static readonly Vector4 Detail = new(0.75f, 0.75f, 0.75f, 1.0f);
  private static readonly Vector4 Muted = new(0.55f, 0.55f, 0.55f, 1.0f);

  public required IQuestPatchService PatchService { get; init; }
  public required IDataService DataService { get; init; }

  private TextNode TitleNode { get; }
  private TextNode StatusNode { get; }
  private TextNode[] FieldNodes { get; }
  private TextNode PlaceholderNode { get; }

  public QuestInfoNode()
  {
    TitleNode = new TextNode { TextFlags = TextFlags.Ellipsis | TextFlags.Emboss, FontSize = 14, TextColor = Heading };
    TitleNode.AttachNode(this);

    StatusNode = new TextNode { TextFlags = TextFlags.Ellipsis, FontSize = 12, TextColor = Detail };
    StatusNode.AttachNode(this);

    FieldNodes = new TextNode[5];
    for (int i = 0; i < FieldNodes.Length; i++)
    {
      FieldNodes[i] = new TextNode { TextFlags = TextFlags.Ellipsis, FontSize = 12, TextColor = Detail };
      FieldNodes[i].AttachNode(this);
    }

    PlaceholderNode = new TextNode
    {
      TextFlags = TextFlags.Ellipsis,
      FontSize = 12,
      TextColor = Muted,
      String = "Select a quest on the left.",
      IsVisible = true
    };
    PlaceholderNode.AttachNode(this);

    SetQuest(null);
  }

  public void SetQuest(Types.Quest? quest)
  {
    bool has = quest is not null;

    PlaceholderNode.IsVisible = !has;
    TitleNode.IsVisible = StatusNode.IsVisible = has;

    foreach (TextNode field in FieldNodes) field.IsVisible = has;

    if (quest is null) return;

    TitleNode.String = quest.Title;

    bool complete = DataService.IsQuestComplete(quest);
    StatusNode.String = complete ? "Complete" : "Not yet done";

    List<string> lines =
    [
      $"Level {quest.Level}",
      quest.Area.Length > 0 ? quest.Area : "—",
      quest.Section.Length > 0 ? quest.Section : "—"
    ];

    string? patch = PatchService.GetPatch(quest.Ids);
    lines.Add(patch is not null ? $"Added in patch {patch}" : "Patch not recorded");

    // Several ids means the quest was reissued and its ids disagree about the
    // patch, so the number above is the earliest of them. Worth stating rather
    // than leaving it to look arbitrary.
    lines.Add(quest.Ids.Count > 1
      ? $"{quest.Ids.Count} ids — showing the earliest patch"
      : quest.Ids.Count == 1 ? $"Quest id {quest.Ids[0]}" : "");

    for (int i = 0; i < FieldNodes.Length; i++)
    {
      FieldNodes[i].String = i < lines.Count ? lines[i] : "";
      FieldNodes[i].IsVisible = i < lines.Count && lines[i].Length > 0;
    }
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    float width = Width - Margin * 2.0f;
    float y = Margin;

    TitleNode.Size = new Vector2(width, 24.0f);
    TitleNode.Position = new Vector2(Margin, y);
    y += 28.0f;

    StatusNode.Size = new Vector2(width, LineHeight);
    StatusNode.Position = new Vector2(Margin, y);
    y += LineHeight + 8.0f;

    foreach (TextNode field in FieldNodes)
    {
      field.Size = new Vector2(width, LineHeight);
      field.Position = new Vector2(Margin, y);
      y += LineHeight;
    }

    PlaceholderNode.Size = new Vector2(width, LineHeight);
    PlaceholderNode.Position = new Vector2(Margin, Height / 2.0f - LineHeight);
  }
}
