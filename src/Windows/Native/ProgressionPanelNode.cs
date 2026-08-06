using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// Class and job levels with experience toward the next level.
/// </summary>
public class ProgressionPanelNode : TabPanelNode
{
  private const float RowHeight = 22.0f;
  private const float NameWidth = 180.0f;
  private const float LevelWidth = 50.0f;
  private const float ExpWidth = 200.0f;

  /// <summary>
  /// Pooled rather than sized from live state, which would be zero if this were
  /// built before a character loaded and would then never grow.
  /// </summary>
  private const int MaxRows = 40;

  private static readonly Vector4 Heading = new(0.6f, 0.8f, 1.0f, 1.0f);
  private static readonly Vector4 Normal = new(1.0f, 1.0f, 1.0f, 1.0f);

  public required IClassJobProgressService ProgressService { get; init; }

  private readonly VerticalListNode _list;
  private readonly List<(TextNode Name, TextNode Level, TextNode Exp)> _rows = [];

  public ProgressionPanelNode()
  {
    _list = new VerticalListNode { ItemSpacing = 2.0f, IsVisible = true };
    _list.AttachNode(this);

    _list.AddNode(BuildRow("Job", "Lv", "Experience", header: true).Container);

    for (int i = 0; i < MaxRows; i++)
    {
      (HorizontalListNode container, TextNode name, TextNode level, TextNode exp) = BuildRow("", "", "");
      _list.AddNode(container);
      _rows.Add((name, level, exp));
    }
  }

  public override void Refresh()
  {
    List<ClassJobProgress> unlocked = [.. ProgressService.GetProgress().Where((p) => p.IsUnlocked)];

    for (int i = 0; i < _rows.Count; i++)
    {
      (TextNode name, TextNode level, TextNode exp) = _rows[i];

      if (i >= unlocked.Count)
      {
        name.IsVisible = level.IsVisible = exp.IsVisible = false;
        continue;
      }

      ClassJobProgress job = unlocked[i];
      name.IsVisible = level.IsVisible = exp.IsVisible = true;

      name.String = job.Name;
      level.String = job.Level.ToString();
      exp.String = job.IsMaxLevel ? "Max level" : $"{job.Experience:N0} / {job.ExperienceToNext:N0}";
    }
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    _list.Size = new Vector2(Width, Height);
    _list.Position = new Vector2(0.0f, 0.0f);
  }

  private static (HorizontalListNode Container, TextNode Name, TextNode Level, TextNode Exp) BuildRow(
    string name, string level, string exp, bool header = false)
  {
    HorizontalListNode container = new()
    {
      Size = new Vector2(NameWidth + LevelWidth + ExpWidth, RowHeight),
      IsVisible = true
    };

    TextNode nameNode = MakeText(name, NameWidth, AlignmentType.Left, header);
    TextNode levelNode = MakeText(level, LevelWidth, AlignmentType.Right, header);
    TextNode expNode = MakeText(exp, ExpWidth, AlignmentType.Right, header);

    container.AddNode(nameNode);
    container.AddNode(levelNode);
    container.AddNode(expNode);

    return (container, nameNode, levelNode, expNode);
  }

  private static TextNode MakeText(string text, float width, AlignmentType alignment, bool header) => new()
  {
    Size = new Vector2(width, RowHeight),
    String = text,
    AlignmentType = alignment,
    FontSize = 12,
    TextColor = header ? Heading : Normal,
    IsVisible = true
  };
}
