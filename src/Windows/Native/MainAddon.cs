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
  public required Configuration Config { get; init; }
  public required ILogger Logger { get; init; }

  private readonly List<TabPanelNode> _panels = [];
  private TabPanelNode? _active;

  protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
  {
    base.OnSetup(addon, atkValueSpan);

    Vector2 panelSize = new(ContentSize.X, ContentSize.Y - TabBarHeight - Gap);
    Vector2 panelPosition = ContentStartPosition + new Vector2(0.0f, TabBarHeight + Gap);

    OverviewPanelNode overview = new() { DataService = DataService };
    QuestsPanelNode quests = new() { DataService = DataService, PatchService = PatchService };
    ProgressionPanelNode progression = new() { ProgressService = ProgressService };
    SettingsPanelNode settings = new() { Config = Config, DataService = DataService };

    TabBarNode tabs = new()
    {
      Position = ContentStartPosition,
      Size = new Vector2(ContentSize.X, TabBarHeight),
      IsVisible = true,
      InitialEntries =
      [
        new TabBarEntry { Label = "Overview", OnClick = () => Show(overview) },
        new TabBarEntry { Label = "Quests", OnClick = () => Show(quests) },
        new TabBarEntry { Label = "Progression", OnClick = () => Show(progression) },
        new TabBarEntry { Label = "Settings", OnClick = () => Show(settings) }
      ]
    };

    AddNode(tabs);

    foreach (TabPanelNode panel in new TabPanelNode[] { overview, quests, progression, settings })
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

    base.OnFinalize(addon);
  }
}
