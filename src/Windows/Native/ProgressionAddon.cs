using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// The Progression tab rebuilt as a native game window.
///
/// This is an experiment, and the point of it is the paradigm rather than the
/// feature. ImGui is immediate mode: every frame you describe the whole
/// interface again and it is drawn from scratch. Native UI is retained mode:
/// nodes are real objects in the game's own display tree, created once in
/// OnSetup and then mutated. Rebuilding them per frame would be both wrong and
/// expensive.
///
/// So the shape of the code inverts. Instead of one draw method that reads state
/// and emits widgets, there is a construction pass that makes nodes and an
/// update pass that changes their text.
/// </summary>
public unsafe class ProgressionAddon : NativeAddon
{
  private const float RowHeight = 22.0f;
  private const float NameWidth = 150.0f;
  private const float LevelWidth = 50.0f;
  private const float ExpWidth = 180.0f;

  /// <summary>
  /// Rows are pooled rather than matched to the current job count. OnSetup runs
  /// once, and if it happened to run while no character was loaded a
  /// state-derived count would be zero and the window would stay empty forever.
  /// </summary>
  private const int MaxRows = 40;

  /// <summary>Set at construction; the addon does not resolve its own services.</summary>
  public required IClassJobProgressService ProgressService { get; init; }

  private VerticalListNode? _list;

  /// <summary>
  /// Rows are kept so OnUpdate can address them directly. In ImGui this list
  /// would not exist — the widgets are gone the moment the frame ends.
  /// </summary>
  private readonly List<(TextNode Name, TextNode Level, TextNode Exp)> _rows = [];

  /// <summary>
  /// Runs once when the window is created. Everything the window will ever show
  /// is built here, sized for the maximum number of rows rather than the current
  /// one, so later updates never have to add or remove nodes.
  /// </summary>
  protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
  {
    _list = new VerticalListNode
    {
      Position = ContentStartPosition,
      Size = ContentSize,
      ItemSpacing = 2.0f,
      IsVisible = true
    };

    AddNode(_list);

    _list.AddNode(BuildRow("Job", "Lv", "Experience", header: true).Container);

    for (int i = 0; i < MaxRows; i++)
    {
      (HorizontalListNode container, TextNode name, TextNode level, TextNode exp) = BuildRow("", "", "");
      _list.AddNode(container);
      _rows.Add((name, level, exp));
    }
  }

  /// <summary>
  /// Runs on the game's update. Only text changes; no node is created, destroyed
  /// or reattached, which is what keeps a retained tree cheap.
  /// </summary>
  protected override void OnUpdate(AtkUnitBase* addon)
  {
    List<ClassJobProgress> progress = ProgressService.GetProgress();
    List<ClassJobProgress> unlocked = [.. progress.Where((p) => p.IsUnlocked)];

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
      exp.String = job.IsMaxLevel
        ? "Max level"
        : $"{job.Experience:N0} / {job.ExperienceToNext:N0}";
    }
  }

  private (HorizontalListNode Container, TextNode Name, TextNode Level, TextNode Exp) BuildRow(
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
    TextColor = header ? new Vector4(0.6f, 0.8f, 1.0f, 1.0f) : new Vector4(1.0f, 1.0f, 1.0f, 1.0f),
    IsVisible = true
  };
}
