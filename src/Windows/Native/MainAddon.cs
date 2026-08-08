using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// Time Memoria as a single native game window: a tab bar, and one panel on
/// screen at a time beneath it.
///
/// The tabs existed as four separate addons first. That was scaffolding — it
/// proved each piece built, updated and unloaded on its own before any of it
/// was combined — and this is what it was scaffolding for.
/// </summary>
public unsafe class MainAddon : NativeAddon
{
  private const float TabBarHeight = 28.0f;
  private const float Gap = 6.0f;

  public required IDataService DataService { get; init; }
  public required IQuestPatchService PatchService { get; init; }
  public required IClassJobProgressService ProgressService { get; init; }
  public required ILedgerExportService LedgerExport { get; init; }
  public required IQuestSnapshotService Snapshot { get; init; }
  public required IPlaytimeService Playtime { get; init; }
  public required IPacingService Pacing { get; init; }
  public required IFestivalService Festivals { get; init; }
  public required INewsService News { get; init; }
  public required IPlayerState PlayerState { get; init; }
  public required Configuration Config { get; init; }
  public required ILogger Logger { get; init; }

  /// <summary>
  /// Raised by the title bar's gear button, which trades this window for the
  /// at-a-glance one. The addon owns neither side of that swap, so it asks.
  /// </summary>
  public required System.Action OnSwapRequested { get; init; }

  private readonly List<TabPanelNode> _panels = [];
  private TabPanelNode? _active;
  private TextureButtonNode? _swapButton;

  protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
  {
    base.OnSetup(addon, atkValueSpan);

    Vector2 panelSize = new(ContentSize.X, ContentSize.Y - TabBarHeight - Gap);
    Vector2 panelPosition = ContentStartPosition + new Vector2(0.0f, TabBarHeight + Gap);

    OverviewPanelNode overview = new() { DataService = DataService };
    QuestsPanelNode quests = new()
    {
      DataService = DataService, PatchService = PatchService, ProgressService = ProgressService,
      Logger = Logger
    };
    NewsPanelNode news = new()
    {
      Playtime = Playtime, Pacing = Pacing, Festivals = Festivals, News = News, PlayerState = PlayerState
    };
    WhatsNewPanelNode whatsNew = new() { Snapshot = Snapshot, PatchService = PatchService };
    ProgressionPanelNode progression = new() { ProgressService = ProgressService, LedgerExport = LedgerExport };
    SettingsPanelNode settings = new() { Config = Config, DataService = DataService };
    HelpPanelNode help = new();
    CreditsPanelNode credits = new();

    TabPanelNode[] panels = [overview, quests, news, whatsNew, progression, settings, help, credits];

    TabBarNode tabs = new()
    {
      Position = ContentStartPosition,
      Size = new Vector2(ContentSize.X, TabBarHeight),
      IsVisible = true,
      InitialEntries =
      [
        new TabBarEntry { Label = "Overview", OnClick = () => Show(overview) },
        new TabBarEntry { Label = "Quests", OnClick = () => Show(quests) },
        new TabBarEntry { Label = "News", OnClick = () => Show(news) },
        new TabBarEntry { Label = "What's New", OnClick = () => Show(whatsNew) },
        new TabBarEntry { Label = "Progression", OnClick = () => Show(progression) },
        new TabBarEntry { Label = "Settings", OnClick = () => Show(settings) },
        new TabBarEntry { Label = "Help", OnClick = () => Show(help) },
        new TabBarEntry { Label = "Credits", OnClick = () => Show(credits) }
      ]
    };

    AddNode(tabs);

    _swapButton = TitleBarButton.Gear(WindowNode, Size.X, "At-a-glance window (/tmmini)", () => OnSwapRequested());
    AddNode(_swapButton);

    foreach (TabPanelNode panel in panels)
    {
      panel.Position = panelPosition;
      panel.Size = panelSize;
      panel.IsVisible = false;

      AddNode(panel);
      _panels.Add(panel);
    }

    // The tab bar starts on its first entry without raising a click, so the
    // matching panel has to be shown here or the window opens blank.
    Show(overview);
  }

  /// <summary>
  /// Panels are kept alive and hidden rather than built and destroyed, so
  /// switching tabs costs a visibility flag rather than a node tree.
  /// </summary>
  private void Show(TabPanelNode panel)
  {
    foreach (TabPanelNode candidate in _panels) candidate.IsVisible = ReferenceEquals(candidate, panel);

    _active = panel;

    panel.OnShown();
    panel.Refresh();
  }

  /// <summary>Only the visible panel is refreshed; a hidden one has nothing worth recomputing.</summary>
  protected override void OnUpdate(AtkUnitBase* addon) => _active?.Refresh();

  protected override void OnFinalize(AtkUnitBase* addon)
  {
    _panels.Clear();
    _active = null;
    _swapButton = null;

    base.OnFinalize(addon);
  }
}
