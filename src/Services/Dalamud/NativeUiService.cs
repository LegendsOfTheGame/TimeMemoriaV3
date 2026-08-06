using KamiToolKit;
using TimeMemoria.Windows.Native;

namespace TimeMemoria.Services;

public interface INativeUiService : IAsyncDisposable
{
  /// <summary>Builds the window. Must run after KamiToolKit is initialised.</summary>
  void Create();

  /// <summary>Shows or hides the native window.</summary>
  void Toggle();

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
  Configuration _configuration, IQuestPatchService _questPatch) : INativeUiService
{
  private MainAddon? _window;

  public bool IsReady => _window is not null;

  public void Create()
  {
    if (_window is not null) return;

    _window = new MainAddon
    {
      InternalName = "TimeMemoria",
      Title = "Time Memoria",
      Size = new Vector2(900.0f, 640.0f),
      DataService = _dataService,
      PatchService = _questPatch,
      ProgressService = _classJobProgress,
      Config = _configuration,
      Logger = _logger
    };

    _logger.Debug("[NativeUi] Window created.");
  }

  public void Toggle()
  {
    if (_window is null)
    {
      _logger.Error("[NativeUi] Toggled before the window was created.");
      return;
    }

    _window.Toggle();
  }

  public async ValueTask DisposeAsync()
  {
    GC.SuppressFinalize(this);

    if (_window is null) return;

    await _window.DisposeAsync();
    _window = null;

    _logger.Debug("[NativeUi] Window disposed.");
  }
}
