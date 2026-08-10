using Dalamud.Bindings.ImGui;

namespace TimeMemoria.Services;

public interface ICommandService : IHostedService;

/// <summary>
/// The plugin's chat commands.
///
/// These were <c>/questtracker</c> and <c>/qt</c>, inherited from the plugin
/// this one descends from — which is still installed for plenty of people,
/// including anyone comparing the two. Registering another plugin's commands is
/// a collision, so they are gone.
/// </summary>
public class CommandService(ILogger _logger, IDataService _dataService, IWindowService _windowService,
  INativeUiService _nativeUi, Configuration _configuration, ICommandManager _commandManager,
  ILedgerExportService _ledgerExport) : ICommandService
{
  private const string MainCommand = "/timememoria";
  private const string MainAlias = "/tm";
  private const string GlanceCommand = "/tmmini";

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _commandManager.AddHandler(MainCommand, new CommandInfo(OnCommand)
    {
      HelpMessage = $"Open Time Memoria. See '{MainCommand} help' for more.",
      ShowInHelp = true
    });

    _commandManager.AddHandler(MainAlias, new CommandInfo(OnCommand)
    {
      HelpMessage = $"Alias for {MainCommand}.",
      ShowInHelp = true
    });

    _commandManager.AddHandler(GlanceCommand, new CommandInfo(OnGlanceCommand)
    {
      HelpMessage = "Open the at-a-glance window: playtime, pacing and jobs.",
      ShowInHelp = true
    });

    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _commandManager.RemoveHandler(MainCommand);
    _commandManager.RemoveHandler(MainAlias);
    _commandManager.RemoveHandler(GlanceCommand);

    return _logger.ServiceLifecycle();
  }

  private void OnGlanceCommand(string command, string arguments) => _nativeUi.ToggleCompanion();

  /// <summary>
  /// Command handlers run on the framework thread, the same one ImGui renders
  /// on, so writing the clipboard from here is safe — the window's copy buttons
  /// do it from the same place.
  /// </summary>
  private void Copy(Func<string> build, string what)
  {
    try
    {
      ImGui.SetClipboardText(build());
      _logger.Chat($"Copied your progress in {what}.");
    }
    catch (Exception ex)
    {
      _logger.Chat($"Copy failed: {ex.Message}");
      _logger.Error(ex, "Clipboard copy failed");
    }
  }

  private void OnCommand(string command, string arguments)
  {
    _logger.Debug($"command::'{command}' arguments::'{arguments}'");

    string[] args = arguments.Split(" ", StringSplitOptions.RemoveEmptyEntries);
    if (args.Length == 0)
    {
      if (_configuration.UseNativeUi) _nativeUi.Toggle();
      else _windowService.Toggle();

      return;
    }

    switch (args[0])
    {
      case "help":
      case "?":
        _logger.Chat("Available commands:");
        _logger.Chat($"  {MainCommand} — open the window you have chosen in Settings");
        _logger.Chat($"  {MainCommand} classic — the classic window, whatever is chosen");
        _logger.Chat($"  {MainCommand} native — the game-styled window, whatever is chosen");
        _logger.Chat($"  {MainCommand} ledger — copy your progress to the clipboard");
        _logger.Chat($"  {MainCommand} reset — reset the quest tree");
        _logger.Chat($"  {GlanceCommand} — open the at-a-glance window");
        break;

      // Straight to the clipboard rather than by way of the window. Nothing
      // leaves the machine either way; this only removes the two clicks between
      // wanting the figures and having them.
      case "ledger":
      case "progress":
        Copy(_ledgerExport.BuildLedgerJson, "ledger format");
        break;

      // Named explicitly so neither window can become unreachable. Choosing the
      // native one and then finding it broken should not leave anybody stuck.
      case "classic":
        _windowService.Toggle();
        break;

      case "native":
        _nativeUi.Toggle();
        break;

      case "reset":
        _dataService.Reset();
        break;

      default:
        _logger.Chat("Invalid command:");
        _logger.Chat($"  {command} {arguments}");
        goto case "help";
    }
  }
}
