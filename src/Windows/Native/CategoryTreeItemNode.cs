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
      AlignmentType = AlignmentType.Right
    };
    PercentNode.AttachNode(this);
  }

  protected override void SetNodeData(QuestData itemData)
  {
    NameNode.String = itemData.Title;

    // A finished section used to turn green, which said it in colour alone; the
    // percentage beside it already reads 100%. Both use the theme's text colour,
    // set here so a theme change reaches pooled rows.
    NameNode.TextColor = PercentNode.TextColor = NativeTheme.Text;

    // Blank rather than an em dash where there is no percentage to show. The dash
    // was meant to read as "not applicable", but the only rows without a total are
    // the synthetic bundles — Oldest unfinished, Job Quests — where a reader is not
    // wondering about a missing figure in the first place. It just added a column of
    // marks down a panel whose other rows all carry real numbers.
    PercentNode.String = itemData.Total > 0
      ? $"{itemData.NumComplete / itemData.Total:P0}"
      : "";
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
