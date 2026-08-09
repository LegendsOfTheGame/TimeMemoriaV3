using KamiToolKit;
using TimeMemoria.Windows.Native;

namespace TimeMemoria.Services;

public interface INativeUiService : IAsyncDisposable
{
  /// <summary>Builds the window. Must run after KamiToolKit is initialised.</summary>
  void Create();

  /// <summary>Shows or hides the native window.</summary>
  void Toggle();

  /// <summary>Shows or hides the small at-a-glance window.</summary>
  void ToggleCompanion();

  /// <summary>Closes the full window and opens the at-a-glance one in its place.</summary>
  void SwapToCompanion();

  /// <summary>Closes the at-a-glance window and opens the full one in its place.</summary>
  void SwapToMain();

  /// <summary>False until <see cref="Create"/> has run.</summary>
  bool IsReady { get; }
}

/// <summary>
/// Owns the native game window.
///
/// Deliberately not a hosted service. The host disposes its services
/// synchronously, and a native addon cannot be torn down that way: closing one
/// plays an animation lasting several frames, so <c>Dispose</c> returns before
/// the addon is actually gone. Awaiting <c>DisposeAsync</c> is the only way to
/// be sure, and that has to happen off the main thread or the game deadlocks
/// waiting for frames that cannot advance.
///
/// So <see cref="Plugin"/> drives this class's lifetime by hand, in the order
/// KamiToolKit requires: the addon awaited first, then the library's own
/// cleanup, which conversely must be back on the main thread.
/// </summary>
public class NativeUiService(ILogger _logger, IClassJobProgressService _classJobProgress, IDataService _dataService,
  Configuration _configuration, IQuestPatchService _questPatch, IPlaytimeService _playtime, IPacingService _pacing,
  IQuestSnapshotService _snapshot, IFestivalService _festivals, INewsService _news, IPlayerState _playerState,
  ILedgerExportService _ledgerExport, IAchievementService _achievements)
  : INativeUiService
{
  private MainAddon? _window;
  private CompanionAddon? _companion;

  public bool IsReady => _window is not null;

  public void Create()
  {
    if (_window is not null) return;

    _companion = new CompanionAddon
    {
      InternalName = "TimeMemoriaGlance",
      Title = "Time Memoria",
      Size = new Vector2(310.0f, 400.0f),
      ProgressService = _classJobProgress,
      Playtime = _playtime,
      Pacing = _pacing,
      Achievements = _achievements,
      DataService = _dataService,
      OnSwapRequested = SwapToMain
    };

    _window = new MainAddon
    {
      InternalName = "TimeMemoria",
      Title = "Time Memoria",
      Size = new Vector2(900.0f, 640.0f),
      DataService = _dataService,
      PatchService = _questPatch,
      ProgressService = _classJobProgress,
      LedgerExport = _ledgerExport,
      Snapshot = _snapshot,
      Playtime = _playtime,
      Pacing = _pacing,
      Festivals = _festivals,
      News = _news,
      PlayerState = _playerState,
      Config = _configuration,
      Logger = _logger,
      OnSwapRequested = SwapToCompanion
    };

    _logger.Debug("[NativeUi] Window created.");
  }

  /// <summary>
  /// KamiToolKit windows have no resize handle, so the size is taken from the
  /// ImGui window instead — that one the player can drag, and this one inherits
  /// whatever they settled on. Size is read when the window is built, which
  /// happens on open, so setting it first is enough.
  /// </summary>
  public void Toggle()
  {
    if (_window is null)
    {
      _logger.Error("[NativeUi] Toggled before the window was created.");
      return;
    }

    ApplyConfiguredSize();

    _window.Toggle();
  }

  /// <summary>
  /// Size is read when the window is built, which happens on open, so this only
  /// has to run before one. It is a no-op on a window that is already open.
  /// </summary>
  private void ApplyConfiguredSize()
  {
    if (_window is null || _window.IsOpen) return;

    Vector2 size = new(
      Math.Clamp(_configuration.NativeWindowWidth, MinimumSize.X, MaximumSize.X),
      Math.Clamp(_configuration.NativeWindowHeight, MinimumSize.Y, MaximumSize.Y));

    if (size == _window.Size) return;

    _window.Size = size;
    _logger.Debug($"[NativeUi] Opening at {size.X:F0}x{size.Y:F0}.");
  }

  /// <summary>
  /// The two windows are alternatives rather than companions, so swapping closes
  /// one before opening the other. Leaving both up would put the same numbers on
  /// screen twice.
  /// </summary>
  public void SwapToCompanion()
  {
    if (_window is null || _companion is null)
    {
      _logger.Error("[NativeUi] Swap requested before the windows were created.");
      return;
    }

    _window.Close();
    _companion.Open();
  }

  /// <inheritdoc cref="SwapToCompanion"/>
  public void SwapToMain()
  {
    if (_window is null || _companion is null)
    {
      _logger.Error("[NativeUi] Swap requested before the windows were created.");
      return;
    }

    _companion.Close();

    ApplyConfiguredSize();
    _window.Open();
  }

  public void ToggleCompanion()
  {
    if (_companion is null)
    {
      _logger.Error("[NativeUi] Companion toggled before it was created.");
      return;
    }

    _companion.Toggle();
  }

  /// <summary>Below this the tree and quest list stop being usable.</summary>
  private static readonly Vector2 MinimumSize = new(700.0f, 460.0f);

  private static readonly Vector2 MaximumSize = new(2400.0f, 1600.0f);

  public async ValueTask DisposeAsync()
  {
    GC.SuppressFinalize(this);

    if (_companion is not null)
    {
      await _companion.DisposeAsync();
      _companion = null;
    }

    if (_window is not null)
    {
      await _window.DisposeAsync();
      _window = null;
    }

    _logger.Debug("[NativeUi] Windows disposed.");
  }
}
