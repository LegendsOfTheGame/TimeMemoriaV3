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
  private const float PercentWidth = 60.0f;

  /// <summary>
  /// Pooled rather than sized from live state, which would be zero if this were
  /// built before a character loaded and would then never grow.
  /// </summary>
  private const int MaxRows = 40;

  private static readonly Vector4 Heading = new(0.6f, 0.8f, 1.0f, 1.0f);
  private static readonly Vector4 Normal = new(1.0f, 1.0f, 1.0f, 1.0f);

  /// <summary>Role colours, following the game's own tank/healer/DPS convention.</summary>
  private static readonly Dictionary<string, Vector4> RoleColours = new()
  {
    ["Tank"] = new(0.34f, 0.58f, 0.92f, 1.0f),
    ["Healer"] = new(0.35f, 0.78f, 0.47f, 1.0f),
    ["DPS"] = new(0.85f, 0.40f, 0.38f, 1.0f),
    ["Crafter"] = new(0.72f, 0.58f, 0.88f, 1.0f),
    ["Gatherer"] = new(0.88f, 0.72f, 0.38f, 1.0f)
  };

  public required IClassJobProgressService ProgressService { get; init; }
  public required ILedgerExportService LedgerExport { get; init; }

  private readonly TextButtonNode _copyLedger;
  private readonly TextButtonNode _openLedger;
  private readonly TextNode _copyFeedback;

  private DateTime _copyShownAt = DateTime.MinValue;

  private readonly VerticalListNode _list;
  private readonly List<(TextNode Name, TextNode Level, TextNode Exp, TextNode Percent)> _rows = [];

  public ProgressionPanelNode()
  {
    // One payload, not two. The ledger export already carries everything the
    // old progression JSON did, so a second button offered a subset of the
    // first under a name that suggested otherwise.
    _copyLedger = MakeButton("Copy for the Ledger",
      () => Copy(LedgerExport.BuildLedgerJson, "Copied in ledger format."));

    _openLedger = MakeButton("Open the Ledger",
      () => Dalamud.Utility.Util.OpenLink(LedgerUrl));

    _copyFeedback = new TextNode
    {
      FontSize = 12,
      TextColor = new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
      String = "Nothing is sent anywhere — the export stays on your clipboard.",
      IsVisible = true
    };
    _copyFeedback.AttachNode(this);

    _list = new VerticalListNode { ItemSpacing = 2.0f, IsVisible = true };
    _list.AttachNode(this);

    _list.AddNode(BuildRow("Job", "Lv", "Experience", "%", header: true).Container);

    for (int i = 0; i < MaxRows; i++)
    {
      (HorizontalListNode container, TextNode name, TextNode level, TextNode exp, TextNode percent) =
        BuildRow("", "", "", "");
      _list.AddNode(container);
      _rows.Add((name, level, exp, percent));
    }
  }

  public override void Refresh()
  {
    // Let a copy confirmation fade rather than sit there for ever.
    if (_copyShownAt != DateTime.MinValue && DateTime.UtcNow - _copyShownAt > TimeSpan.FromSeconds(4))
    {
      _copyFeedback.String = "Nothing is sent anywhere — the export stays on your clipboard.";
      _copyShownAt = DateTime.MinValue;
    }

    List<ClassJobProgress> unlocked = [.. ProgressService.GetProgress().Where((p) => p.IsUnlocked)];

    // The least progressed job in each role. Compared on level plus the fraction
    // through it — two jobs at the same level are not equally far along, and the
    // one with less experience is the one actually worth levelling.
    Dictionary<string, float> lowestByRole = unlocked
      .GroupBy((p) => p.Role)
      .ToDictionary((g) => g.Key, (g) => g.Min(Effective));

    for (int i = 0; i < _rows.Count; i++)
    {
      (TextNode name, TextNode level, TextNode exp, TextNode percent) = _rows[i];

      if (i >= unlocked.Count)
      {
        name.IsVisible = level.IsVisible = exp.IsVisible = percent.IsVisible = false;
        continue;
      }

      ClassJobProgress job = unlocked[i];
      name.IsVisible = level.IsVisible = exp.IsVisible = percent.IsVisible = true;

      bool isLowest = lowestByRole.TryGetValue(job.Role, out float lowest)
                      && Math.Abs(Effective(job) - lowest) < 0.0005f;

      // The marker carries the meaning on its own, so the colour is free to say
      // something else — which role the job belongs to.
      name.String = isLowest ? $"> {job.Name}" : $"   {job.Name}";
      name.TextColor = RoleColours.GetValueOrDefault(job.Role, Normal);

      level.String = job.Level.ToString();
      exp.String = job.IsMaxLevel ? "Max level" : $"{job.Experience:N0} / {job.ExperienceToNext:N0}";
      percent.String = job.IsMaxLevel ? "—" : $"{job.Fraction:P1}";
    }
  }

  /// <summary>Where the ledger export is meant to be pasted.</summary>
  private const string LedgerUrl = "https://legendsofthegame.github.io/pandora-lunar/";

  private TextButtonNode MakeButton(string label, System.Action onClick)
  {
    TextButtonNode node = new() { String = label, IsVisible = true, OnClick = onClick };
    node.AttachNode(this);

    return node;
  }

  /// <summary>
  /// Builds a payload and puts it on the clipboard, reporting either way. This
  /// runs from a button callback on the framework thread, which is the same
  /// thread ImGui renders on, so the clipboard call is safe from here.
  /// </summary>
  private void Copy(Func<string> build, string success)
  {
    try
    {
      ImGui.SetClipboardText(build());
      _copyFeedback.String = success;
    }
    catch (Exception ex)
    {
      _copyFeedback.String = $"Copy failed: {ex.Message}";
    }

    _copyShownAt = DateTime.UtcNow;
  }

  /// <summary>Level plus progress through it, rounded the way the export rounds it.</summary>
  private static float Effective(ClassJobProgress job)
    => job.Level + (float)Math.Round(job.Fraction, 3, MidpointRounding.AwayFromZero);

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    const float buttonHeight = 26.0f;
    const float buttonWidth = 150.0f;
    const float gap = 6.0f;

    // Buttons along the bottom, table above them.
    float buttonsY = Height - buttonHeight;

    _copyLedger.Size = new Vector2(buttonWidth, buttonHeight);
    _copyLedger.Position = new Vector2(0.0f, buttonsY);

    _openLedger.Size = new Vector2(buttonWidth, buttonHeight);
    _openLedger.Position = new Vector2(buttonWidth + gap, buttonsY);

    _copyFeedback.Size = new Vector2(Width, 18.0f);
    _copyFeedback.Position = new Vector2(0.0f, buttonsY - 20.0f);

    _list.Size = new Vector2(Width, buttonsY - 24.0f);
    _list.Position = new Vector2(0.0f, 0.0f);
  }

  private static (HorizontalListNode Container, TextNode Name, TextNode Level, TextNode Exp, TextNode Percent)
    BuildRow(string name, string level, string exp, string percent, bool header = false)
  {
    HorizontalListNode container = new()
    {
      Size = new Vector2(NameWidth + LevelWidth + ExpWidth + PercentWidth, RowHeight),
      IsVisible = true
    };

    TextNode nameNode = MakeText(name, NameWidth, AlignmentType.Left, header);
    TextNode levelNode = MakeText(level, LevelWidth, AlignmentType.Right, header);
    TextNode expNode = MakeText(exp, ExpWidth, AlignmentType.Right, header);
    TextNode percentNode = MakeText(percent, PercentWidth, AlignmentType.Right, header);

    container.AddNode(nameNode);
    container.AddNode(levelNode);
    container.AddNode(expNode);
    container.AddNode(percentNode);

    return (container, nameNode, levelNode, expNode, percentNode);
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
