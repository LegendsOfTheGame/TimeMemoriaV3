namespace TimeMemoria.Services;

public interface IWindowService : IHostedService
{
  void Toggle();
}

public class WindowService(ILogger _logger, IDataService _dataService, MainWindow _mainWindow, WindowSystem _windowSystem,
  IDalamudPluginInterface _pluginInterface, INativeUiService _nativeUi, Configuration _configuration) : IWindowService
{
  public Task StartAsync(CancellationToken cancellationToken)
  {
    _windowSystem.AddWindow(_mainWindow);

    _pluginInterface.UiBuilder.DisableCutsceneUiHide = true;
    _pluginInterface.UiBuilder.Draw += UiBuilderOnDraw;
    _pluginInterface.UiBuilder.OpenConfigUi += OpenSettings;
    _pluginInterface.UiBuilder.OpenMainUi += OpenMain;

    _dataService.OnReset += _mainWindow.Reset;

#if DEBUG
    _mainWindow.IsOpen = true; ;
#endif

    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _pluginInterface.UiBuilder.OpenConfigUi -= OpenSettings;
    _pluginInterface.UiBuilder.OpenMainUi -= OpenMain;
    _pluginInterface.UiBuilder.Draw -= UiBuilderOnDraw;

    _windowSystem.RemoveAllWindows();
    _dataService.OnReset -= _mainWindow.Reset;

    return _logger.ServiceLifecycle();
  }

  public void Toggle()
  {
    _mainWindow.Toggle();
  }

  /// <summary>
  /// The installer's Open button. Opens the window chosen in Settings, the same
  /// one <c>/tm</c> opens, and never closes it. The classic window is the
  /// fallback until the native one exists, so the button always does something.
  /// </summary>
  private void OpenMain()
  {
    if (_configuration.UseNativeUi && _nativeUi.IsReady) _nativeUi.Open();
    else _mainWindow.IsOpen = true;
  }

  /// <summary>The installer's Settings button: the chosen window, on its Settings tab.</summary>
  private void OpenSettings()
  {
    if (_configuration.UseNativeUi && _nativeUi.IsReady) _nativeUi.OpenSettings();
    else _mainWindow.OpenSettings();
  }

  private void UiBuilderOnDraw()
  {
    _windowSystem.Draw();
  }
}
