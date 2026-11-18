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
  public required IQuestMapService MapService { get; init; }
  public required IClassJobProgressService ProgressService { get; init; }
  public required ILedgerExportService LedgerExport { get; init; }
  public required IQuestSnapshotService Snapshot { get; init; }
  public required IPlaytimeService Playtime { get; init; }
  public required IPacingService Pacing { get; init; }
  public required IFestivalService Festivals { get; init; }
  public required INewsService News { get; init; }
  public required ILanguageService Language { get; init; }
  public required IPlayerState PlayerState { get; init; }
  public required IFoodService Food { get; init; }
  public required IAchievementService Achievements { get; init; }
  public required IFeatureService Features { get; init; }
  public required IQuestJournalService Journal { get; init; }
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
  private QuestsPanelNode? _questsPanel;
  private SettingsPanelNode? _settingsPanel;
  private TabBarNode? _tabs;

  /// <summary>
  /// The Settings tab asked for before <see cref="OnSetup"/> ran. Same race as
  /// <see cref="_pendingUnfinished"/>, same fix.
  /// </summary>
  private bool _pendingSettings;

  /// <summary>The Bonuses tab asked for before <see cref="OnSetup"/> ran (<c>/tm eat</c>). Same race, same fix.</summary>
  private bool _pendingBonuses;

  private BonusesPanelNode? _bonusesPanel;

  /// <summary>
  /// A bundle asked for before the addon finished allocating. Native addon
  /// creation is not synchronous with <see cref="KamiToolKit.BaseTypes.NativeAddon.Open"/>
  /// — the actual <see cref="OnSetup"/> callback lands on a later frame — so a
  /// caller opening a closed window and asking for a bundle in the same breath
  /// would otherwise race <see cref="OnSetup"/>'s own default tab and lose,
  /// landing on Overview instead. Held here and applied once <see cref="OnSetup"/>
  /// actually runs.
  /// </summary>
  private (string Title, List<Types.Quest> Quests, string EmptyMessage)? _pendingUnfinished;

  protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
  {
    base.OnSetup(addon, atkValueSpan);

    // Forced: the window is about to draw totals someone opened it to read.
    DataService.UpdateQuestData(true);

    Vector2 panelSize = new(ContentSize.X, ContentSize.Y - TabBarHeight - Gap);
    Vector2 panelPosition = ContentStartPosition + new Vector2(0.0f, TabBarHeight + Gap);

    OverviewPanelNode overview = new() { DataService = DataService, Achievements = Achievements };
    QuestsPanelNode quests = new()
    {
      DataService = DataService, PatchService = PatchService, MapService = MapService, ProgressService = ProgressService,
      Logger = Logger, Config = Config
    };
    NewsPanelNode news = new()
    {
      Playtime = Playtime, Pacing = Pacing, Festivals = Festivals, News = News, Language = Language,
      PlayerState = PlayerState, Snapshot = Snapshot, PatchService = PatchService, DataService = DataService,
      Logger = Logger
    };
    BonusesPanelNode bonuses = new() { Food = Food };
    ProgressionPanelNode progression = new() { ProgressService = ProgressService, LedgerExport = LedgerExport, Achievements = Achievements };
    // Journal before Features: Features' init builds the rows, and the order here is the order they are set.
    SettingsPanelNode settings = new() { Config = Config, DataService = DataService, Journal = Journal, Features = Features };
    HelpPanelNode help = new();
    CreditsPanelNode credits = new();

    _questsPanel = quests;
    _settingsPanel = settings;
    _bonusesPanel = bonuses;

    TabPanelNode[] panels = [overview, quests, news, bonuses, progression, settings, help, credits];

    TabBarNode tabs = new()
    {
      Position = ContentStartPosition,
      Size = new Vector2(ContentSize.X, TabBarHeight),
      IsVisible = true,
      InitialEntries =
      [
        new TabBarEntry { Label = "Overview", OnClick = () => Show(overview) },
        new TabBarEntry { Label = "Quests", OnClick = () => Show(quests) },
        new TabBarEntry { Label = "News/What's New", OnClick = () => Show(news) },
        new TabBarEntry { Label = "Bonuses", OnClick = () => Show(bonuses) },
        new TabBarEntry { Label = "Progression", OnClick = () => Show(progression) },
        new TabBarEntry { Label = "Settings", OnClick = () => Show(settings) },
        new TabBarEntry { Label = "Help", OnClick = () => Show(help) },
        new TabBarEntry { Label = "Credits", OnClick = () => Show(credits) }
      ]
    };

    _tabs = tabs;
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
    // matching panel has to be shown here or the window opens blank — unless
    // something asked for a bundle before setup finished, which wins instead.
    if (_pendingUnfinished is { } pending)
    {
      _pendingUnfinished = null;
      _pendingSettings = false;
      _pendingBonuses = false;
      ShowUnfinished(pending.Title, pending.Quests, pending.EmptyMessage);
    }
    else if (_pendingSettings)
    {
      _pendingSettings = false;
      _pendingBonuses = false;
      OpenSettings();
    }
    else if (_pendingBonuses)
    {
      _pendingBonuses = false;
      OpenBonuses();
    }
    else
    {
      Show(overview);
    }
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

  /// <summary>
  /// Switches to the Quests tab already showing one synthetic bundle, for
  /// <c>/tm &lt;expansion&gt;</c>. If setup has not run yet — the window was
  /// just opened this same call — the request is queued and applied from
  /// <see cref="OnSetup"/> instead of being dropped.
  /// </summary>
  public void ShowUnfinished(string title, List<Types.Quest> quests, string emptyMessage)
  {
    if (_questsPanel is null)
    {
      _pendingUnfinished = (title, quests, emptyMessage);
      return;
    }

    Show(_questsPanel);
    _questsPanel.SelectBundle(title, quests, emptyMessage);
    _tabs?.SelectTab("Quests");
  }

  /// <summary>
  /// Switches to the Settings tab, for the installer's Settings button. Queued
  /// for <see cref="OnSetup"/> if the window was opened in this same call.
  /// </summary>
  public void OpenSettings()
  {
    if (_settingsPanel is null)
    {
      _pendingSettings = true;
      return;
    }

    Show(_settingsPanel);
    _tabs?.SelectTab("Settings");
  }

  /// <summary>
  /// Switches to the Bonuses tab, for <c>/tm eat</c> when there is a choice to
  /// make. Queued for <see cref="OnSetup"/> if the window was opened in this same call.
  /// </summary>
  public void OpenBonuses()
  {
    if (_bonusesPanel is null)
    {
      _pendingBonuses = true;
      return;
    }

    Show(_bonusesPanel);
    _tabs?.SelectTab("Bonuses");
  }

  /// <summary>
  /// Only the visible panel is refreshed; a hidden one has nothing worth
  /// recomputing. OnUpdate runs every frame while the window exists.
  ///
  /// The recount is what keeps the totals moving while the window is open. It
  /// used to happen only as a side effect of the classic window's draw loop, so
  /// anyone using this window saw figures frozen at login — and session pacing
  /// read zero forever, because the total it subtracts from never changed.
  /// </summary>
  protected override void OnUpdate(AtkUnitBase* addon)
  {
    DataService.UpdateQuestData();

    // Once per clock second, not once per frame. A panel rebuilds every row it
    // shows, and the Bonuses tab measured 1.7 ms a frame doing it (13 Nov 2026)
    // for figures that change no faster than a seconds display. Keyed to the
    // second rather than an interval so a countdown ticks evenly. Show() still
    // refreshes at once, so a tab never opens on stale rows.
    long second = Environment.TickCount64 / 1000;
    if (second != _refreshedSecond)
    {
      _refreshedSecond = second;
      _active?.Refresh();
    }
  }

  private long _refreshedSecond;

  protected override void OnFinalize(AtkUnitBase* addon)
  {
    _panels.Clear();
    _active = null;
    _swapButton = null;

    // Cleared so that a request made after the window closes is queued for the
    // next OnSetup, instead of acting on a panel that no longer exists.
    _settingsPanel = null;
    _bonusesPanel = null;

    base.OnFinalize(addon);
  }
}
