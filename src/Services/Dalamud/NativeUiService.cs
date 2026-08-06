using KamiToolKit;
using KamiToolKit.BaseTypes;
using TimeMemoria.Windows.Native;

namespace TimeMemoria.Services;

public interface INativeUiService : IAsyncDisposable
{
  /// <summary>Builds the addons. Must run after KamiToolKit is initialised.</summary>
  void Create();

  /// <summary>Shows or hides the native Progression window.</summary>
  void ToggleProgression();

  /// <summary>Shows or hides the native Overview window.</summary>
  void ToggleOverview();

  /// <summary>Shows or hides the native Settings window.</summary>
  void ToggleSettings();

  /// <summary>Shows or hides the native quest browser.</summary>
  void ToggleQuests();

  /// <summary>False until <see cref="Create"/> has run.</summary>
  bool IsReady { get; }
}

/// <summary>
/// Owns the native game windows.
///
/// Deliberately not a hosted service. The host disposes its services
/// synchronously, and a native addon cannot be torn down that way: closing one
/// plays an animation that takes several frames, so <c>Dispose</c> returns
/// before the addon is actually gone. Awaiting <c>DisposeAsync</c> instead is
/// the only way to be sure, and that has to happen off the main thread or the
/// game deadlocks waiting for frames that cannot advance.
///
/// So <see cref="Plugin"/> drives this class's lifetime by hand, in the order
/// KamiToolKit requires: every addon awaited first, then the library's own
/// cleanup, which conversely *must* be back on the main thread.
/// </summary>
public class NativeUiService(ILogger _logger, IClassJobProgressService _classJobProgress, IDataService _dataService,
  Configuration _configuration, IQuestPatchService _questPatch) : INativeUiService
{
  private ProgressionAddon? _progression;
  private OverviewAddon? _overview;
  private SettingsAddon? _settings;
  private QuestBrowserAddon? _quests;

  public bool IsReady => _progression is not null;

  public void Create()
  {
    if (_progression is not null) return;

    _progression = new ProgressionAddon
    {
      InternalName = "TimeMemoriaProgression",
      Title = "Time Memoria — Progression",
      Size = new Vector2(460.0f, 520.0f),
      ProgressService = _classJobProgress
    };

    _overview = new OverviewAddon
    {
      InternalName = "TimeMemoriaOverview",
      Title = "Time Memoria — Overview",
      Size = new Vector2(440.0f, 500.0f),
      DataService = _dataService
    };

    _settings = new SettingsAddon
    {
      InternalName = "TimeMemoriaSettings",
      Title = "Time Memoria — Settings",
      Size = new Vector2(420.0f, 340.0f),
      Config = _configuration,
      DataService = _dataService
    };

    _quests = new QuestBrowserAddon
    {
      InternalName = "TimeMemoriaQuests",
      Title = "Time Memoria — Quests",
      Size = new Vector2(830.0f, 620.0f),
      DataService = _dataService,
      PatchService = _questPatch
    };

    _logger.Debug("[NativeUi] Addons created.");
  }

  public void ToggleQuests() => Toggle(_quests, "Quests");

  public void ToggleProgression() => Toggle(_progression, "Progression");

  public void ToggleOverview() => Toggle(_overview, "Overview");

  public void ToggleSettings() => Toggle(_settings, "Settings");

  private void Toggle(NativeAddon? addon, string name)
  {
    if (addon is null)
    {
      _logger.Error($"[NativeUi] {name} toggled before the addons were created.");
      return;
    }

    addon.Toggle();
  }

  /// <summary>
  /// Every addon is awaited individually. Closing one plays an animation over
  /// several frames, so a synchronous Dispose would return while it is still
  /// alive -- and the library cleanup that follows would then be tearing down
  /// nodes still in use.
  /// </summary>
  public async ValueTask DisposeAsync()
  {
    GC.SuppressFinalize(this);

    if (_quests is not null)
    {
      await _quests.DisposeAsync();
      _quests = null;
    }

    if (_settings is not null)
    {
      await _settings.DisposeAsync();
      _settings = null;
    }

    if (_overview is not null)
    {
      await _overview.DisposeAsync();
      _overview = null;
    }

    if (_progression is not null)
    {
      await _progression.DisposeAsync();
      _progression = null;
    }

    _logger.Debug("[NativeUi] Addons disposed.");
  }
}
