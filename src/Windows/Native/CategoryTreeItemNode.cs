using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Interfaces;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// A selectable branch of the quest tree — a category or a genre — shown as its
/// name with how much of it is done.
///
/// The left panel navigates; it does not list quests. Selecting one of these
/// fills the panel on the right, which is the same split the ImGui window uses.
/// </summary>
public class CategoryTreeItemNode : TreeListItemNode<QuestData>, ITreeListItemNode
{
  public static float ItemHeight => 24.0f;

  private static readonly Vector4 Normal = new(1.0f, 1.0f, 1.0f, 1.0f);
  private static readonly Vector4 Complete = new(0.55f, 0.78f, 0.55f, 1.0f);
  private static readonly Vector4 Percent = new(0.65f, 0.65f, 0.65f, 1.0f);

  private TextNode NameNode { get; }
  private TextNode PercentNode { get; }

  public CategoryTreeItemNode()
  {
    NameNode = new TextNode { TextFlags = TextFlags.Ellipsis, FontSize = 12 };
    NameNode.AttachNode(this);

    PercentNode = new TextNode
    {
      TextFlags = TextFlags.Ellipsis,
      FontSize = 12,
      TextColor = Percent,
      AlignmentType = AlignmentType.Right
    };
    PercentNode.AttachNode(this);
  }

  protected override void SetNodeData(QuestData itemData)
  {
    NameNode.String = itemData.Title;

    bool finished = itemData.Total > 0 && itemData.NumComplete >= itemData.Total;
    NameNode.TextColor = finished ? Complete : Normal;

    PercentNode.String = itemData.Total > 0
      ? $"{itemData.NumComplete / itemData.Total:P0}"
      : "—";
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    const float percentWidth = 48.0f;

    NameNode.Size = new Vector2(Width - percentWidth - 14.0f, Height);
    NameNode.Position = new Vector2(10.0f, 0.0f);

    PercentNode.Size = new Vector2(percentWidth, Height);
    PercentNode.Position = new Vector2(Width - percentWidth - 4.0f, 0.0f);
  }
}
