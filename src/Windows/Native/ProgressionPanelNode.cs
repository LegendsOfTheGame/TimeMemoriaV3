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


  public required IClassJobProgressService ProgressService { get; init; }
  public required ILedgerExportService LedgerExport { get; init; }
  public required IAchievementService Achievements { get; init; }

  /// <summary>The only buttons that ask the game server for something; see AchievementService.</summary>
  private readonly TextButtonNode _readCounts;
  private readonly TextButtonNode _readAndCopy;

  /// <summary>
  /// The result of a read-and-copy, shown on the next refresh. The run can end
  /// after the window has closed and its nodes are gone, so the callback must
  /// not touch a node.
  /// </summary>
  private string? _readAndCopyResult;
  private string _countsTooltip = "";
  private bool _countsRunning;

  private readonly TextButtonNode _copyLedger;
  private readonly TextButtonNode _openLedger;
  private readonly TextButtonNode _copyEverything;
  private readonly TextNode _copyFeedback;

  private DateTime _copyShownAt = DateTime.MinValue;

  private readonly ScrollingNode<VerticalListNode> _scroll;
  private readonly List<(HorizontalListNode Container, TextNode Name, TextNode Level, TextNode Exp, TextNode Percent)> _rows = [];

  /// <summary>Unlocked-job count last time the scroll range was measured.</summary>
  private int _lastRowCount = -1;

  private const string CountsHelp = "Asks the game for your gathered, crafted and duty counts, and for your count in each \"Mark of the ...\" achievement, one at a time (about 12 seconds). The export then carries them for the Ledger.";

  public ProgressionPanelNode()
  {
    // One payload, not two. The ledger export already carries everything the
    // old progression JSON did, so a second button offered a subset of the
    // first under a name that suggested otherwise.
    //
    // The callback below closes over LedgerExport, a `required` property not
    // yet assigned at this point in construction (the object initializer sets
    // it only after this constructor returns) — see SettingsPanelNode's
    // constructor for the full explanation. Safe here for the same reason:
    // the button only invokes this on a click, never during construction.
#pragma warning disable CS8602
    _copyLedger = MakeButton("Copy for the Ledger",
      () => Copy(LedgerExport.BuildLedgerJson, $"Copied in ledger format. {Achievements.CountsAge}"));

    // Both in one paste, for when the Ledger should take everything at once.
    _copyEverything = MakeButton("Copy everything",
      () => Copy(LedgerExport.BuildFullJson, $"Copied progress and collections together. {Achievements.CountsAge}"));

    _readCounts = MakeButton("Read achievement counts", () => Achievements.RequestCounts());

    // Copies only when the read ends, so the paste never holds older counts.
    _readAndCopy = MakeButton("Read counts, copy everything", () => Achievements.RequestCounts(() =>
    {
      try
      {
        ImGui.SetClipboardText(LedgerExport.BuildFullJson());
        _readAndCopyResult = $"Copied progress and collections together. {Achievements.CountsAge}";
      }
      catch (Exception ex)
      {
        _readAndCopyResult = $"Copy failed: {ex.Message}";
      }
    }));
    _readAndCopy.TextTooltip = "Reads the achievement counts (about 12 seconds), then copies everything to your clipboard.";
#pragma warning restore CS8602

    _openLedger = MakeButton("Open the Ledger",
      () => Dalamud.Utility.Util.OpenLink(MainWindow.LedgerUrl));

    _copyFeedback = new TextNode
    {
      FontSize = 12,
      TextColor = NativeTheme.Text,
      String = "Nothing is sent anywhere — the export stays on your clipboard.",
      IsVisible = true
    };
    _copyFeedback.AttachNode(this);

    // ContentNode properties are set before Size, as ScrollingNode requires.
    _scroll = new ScrollingNode<VerticalListNode> { IsVisible = true, AutoHideScrollBar = true };
    _scroll.ContentNode.ItemSpacing = 2.0f;
    _scroll.ContentNode.FitContents = true;
    _scroll.AttachNode(this);

    _scroll.ContentNode.AddNode(BuildRow("Job", "Lv", "Experience", "%", header: true).Container);

    for (int i = 0; i < MaxRows; i++)
    {
      (HorizontalListNode container, TextNode name, TextNode level, TextNode exp, TextNode percent) =
        BuildRow("", "", "", "");
      _scroll.ContentNode.AddNode(container);
      _rows.Add((container, name, level, exp, percent));
    }

    _scroll.ScrollToStart();
  }

  public override void Refresh()
  {
    // Let a copy confirmation fade rather than sit there for ever.
    if (_copyShownAt != DateTime.MinValue && DateTime.UtcNow - _copyShownAt > TimeSpan.FromSeconds(4))
    {
      _copyFeedback.String = "Nothing is sent anywhere — the export stays on your clipboard.";
      _copyShownAt = DateTime.MinValue;
    }

    string status = Achievements.RequestStatus;
    bool running = Achievements.RequestRunning;

    // The feedback line shows a run as it happens, then its result until the
    // usual fade. The last read time stays in the button's tooltip.
    if (running)
    {
      if (_copyFeedback.String.ToString() != status) _copyFeedback.String = status;
    }
    else
    {
      if (_readAndCopyResult is { } result)
      {
        _copyFeedback.String = result;
        _copyShownAt = DateTime.UtcNow;
        _readAndCopyResult = null;
      }
      else if (_countsRunning)
      {
        if (status.Length > 0) _copyFeedback.String = status;
        _copyShownAt = DateTime.UtcNow;
      }

      string tooltip = status.Length > 0 ? $"{CountsHelp}\n{status}" : CountsHelp;
      if (tooltip != _countsTooltip)
      {
        _countsTooltip = tooltip;
        _readCounts.TextTooltip = tooltip;
      }
    }

    _countsRunning = running;
    _readCounts.IsEnabled = !running;
    _readAndCopy.IsEnabled = !running;

    List<ClassJobProgress> unlocked = [.. ProgressService.GetProgress().Where((p) => p.IsUnlocked)];

    // The least progressed job in each role. Compared on level plus the fraction
    // through it — two jobs at the same level are not equally far along, and the
    // one with less experience is the one actually worth levelling.
    Dictionary<string, float> lowestByRole = unlocked
      .GroupBy((p) => p.Role)
      .ToDictionary((g) => g.Key, (g) => g.Min(Effective));

    for (int i = 0; i < _rows.Count; i++)
    {
      (HorizontalListNode container, TextNode name, TextNode level, TextNode exp, TextNode percent) = _rows[i];

      if (i >= unlocked.Count)
      {
        container.IsVisible = name.IsVisible = level.IsVisible = exp.IsVisible = percent.IsVisible = false;
        continue;
      }

      ClassJobProgress job = unlocked[i];
      container.IsVisible = name.IsVisible = level.IsVisible = exp.IsVisible = percent.IsVisible = true;

      bool isLowest = lowestByRole.TryGetValue(job.Role, out float lowest)
                      && Math.Abs(Effective(job) - lowest) < 0.0005f;

      // The marker carries the meaning on its own. The name used to be in its
      // role's colour too; that fell below 4.5:1 on light themes, so every
      // column now uses the theme's text colour, set here so it follows a
      // theme change.
      name.String = isLowest ? $"> {job.Name}" : $"   {job.Name}";
      name.TextColor = level.TextColor = exp.TextColor = percent.TextColor = NativeTheme.Text;

      level.String = job.Level.ToString();
      exp.String = job.IsMaxLevel ? "Max level" : $"{job.Experience:N0} / {job.ExperienceToNext:N0}";
      percent.String = job.IsMaxLevel ? "—" : $"{job.Fraction:P1}";
    }

    // Guarded on the count, since Refresh runs every frame and re-measuring
    // walks the whole pool. The scroll range only becomes wrong when the
    // number of unlocked jobs changes.
    if (unlocked.Count != _lastRowCount)
    {
      _lastRowCount = unlocked.Count;

      // Layout first. RecalculateSizes measures the content before it lays it
      // out, so without this the scroll range still counts the hidden rows and
      // the list scrolls past its end.
      _scroll.ContentNode.RecalculateLayout();
      _scroll.RecalculateSizes();
    }
  }


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

    _copyEverything.Size = new Vector2(buttonWidth, buttonHeight);
    _copyEverything.Position = new Vector2(2 * (buttonWidth + gap), buttonsY);

    // The two longest labels, and the last in the row, so they can be wider.
    const float readWidth = 190.0f;
    _readCounts.Size = new Vector2(readWidth, buttonHeight);
    _readCounts.Position = new Vector2(3 * (buttonWidth + gap), buttonsY);

    _readAndCopy.Size = new Vector2(220.0f, buttonHeight);
    _readAndCopy.Position = new Vector2(3 * (buttonWidth + gap) + readWidth + gap, buttonsY);

    _copyFeedback.Size = new Vector2(Width, 18.0f);
    _copyFeedback.Position = new Vector2(0.0f, buttonsY - 20.0f);

    _scroll.Size = new Vector2(Width, buttonsY - 24.0f);
    _scroll.Position = new Vector2(0.0f, 0.0f);
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

  /// <summary>The header row is larger rather than coloured, like every native heading.</summary>
  private static TextNode MakeText(string text, float width, AlignmentType alignment, bool header)
  {
    TextNode node = new()
    {
      Size = new Vector2(width, RowHeight),
      String = text,
      AlignmentType = alignment,
      FontSize = 12,
      TextColor = NativeTheme.Text,
      IsVisible = true
    };

    NativeTheme.SetHeadingSize(node, header);
    return node;
  }
}
