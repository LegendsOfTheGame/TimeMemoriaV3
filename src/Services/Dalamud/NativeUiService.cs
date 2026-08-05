using KamiToolKit;
using TimeMemoria.Windows.Native;

namespace TimeMemoria.Services;

public interface INativeUiService : IAsyncDisposable
{
  /// <summary>Builds the addons. Must run after KamiToolKit is initialised.</summary>
  void Create();

  /// <summary>Shows or hides the native Progression window.</summary>
  void ToggleProgression();

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
public class NativeUiService(ILogger _logger, IClassJobProgressService _classJobProgress) : INativeUiService
{
  private ProgressionAddon? _progression;

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

    _logger.Debug("[NativeUi] Addons created.");
  }

  public void ToggleProgression()
  {
    if (_progression is null)
    {
      _logger.Error("[NativeUi] Toggle requested before the addons were created.");
      return;
    }

    _progression.Toggle();
  }

  public async ValueTask DisposeAsync()
  {
    GC.SuppressFinalize(this);

    if (_progression is null) return;

    await _progression.DisposeAsync();
    _progression = null;

    _logger.Debug("[NativeUi] Addons disposed.");
  }
}
