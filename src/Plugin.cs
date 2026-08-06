using KamiToolKit;
using Microsoft.Extensions.Logging;
using ILogger = TimeMemoria.Services.ILogger;

namespace TimeMemoria;

/// <summary>
/// Implemented as <see cref="IAsyncDalamudPlugin"/> rather than
/// <see cref="IDalamudPlugin"/> because native windows cannot be disposed
/// synchronously. See <see cref="DisposeAsync"/>.
/// </summary>
public sealed class Plugin : IAsyncDalamudPlugin
{
  private readonly IHost _host;
  private readonly IFramework _framework;
  private readonly IDalamudPluginInterface _pluginInterface;

  public Plugin(
    IChatGui chatGui,
    IGameGui gameGui,
    IToastGui toastGui,
    IPluginLog pluginLog,
    IFramework framework,
    IPlayerState playerState,
    IClientState clientState,
    IDataManager dataManager,
    ICommandManager commandManager,
    IDalamudPluginInterface pluginInterface,
    INotificationManager notificationManager
  )
  {
    _framework = framework;
    _pluginInterface = pluginInterface;

    _host = new HostBuilder()
      .UseContentRoot(pluginInterface.ConfigDirectory.FullName)
      .ConfigureLogging(lb =>
      {
        lb.ClearProviders();
        lb.SetMinimumLevel(LogLevel.Trace);
      })
      .ConfigureServices(collection =>
      {
        collection.AddSingleton(chatGui);
        collection.AddSingleton(gameGui);
        collection.AddSingleton(toastGui);
        collection.AddSingleton(pluginLog);
        collection.AddSingleton(framework);
        collection.AddSingleton(playerState);
        collection.AddSingleton(clientState);
        collection.AddSingleton(dataManager);
        collection.AddSingleton(commandManager);
        collection.AddSingleton(pluginInterface);
        collection.AddSingleton(notificationManager);

        collection.AddSingleton<MainWindow>();

        collection.AddSingleton<ILogger, Logger>();
        collection.AddSingleton<IDataService, DataService>();
        collection.AddSingleton<IClassJobProgressService, ClassJobProgressService>();
        collection.AddSingleton<ILedgerExportService, LedgerExportService>();
        collection.AddSingleton<INewsService, NewsService>();
        collection.AddSingleton<IPlaytimeService, PlaytimeService>();
        collection.AddSingleton<ITocService, TocService>();
        collection.AddSingleton<IPacingService, PacingService>();
        collection.AddSingleton<IFestivalService, FestivalService>();
        collection.AddSingleton<IQuestJournalService, QuestJournalService>();
        collection.AddSingleton<IQuestSnapshotService, QuestSnapshotService>();
        collection.AddSingleton<IQuestPatchService, QuestPatchService>();
        collection.AddSingleton<IWindowService, WindowService>();
        collection.AddSingleton<ICommandService, CommandService>();

        // Not registered as a hosted service on purpose -- its disposal has to
        // be awaited, and the host cannot do that. Plugin owns it instead.
        collection.AddSingleton<INativeUiService, NativeUiService>();

        collection.AddSingleton(InitializeConfiguration);
        collection.AddSingleton(new WindowSystem(pluginInterface.InternalName));

        collection.AddHostedService(sp => sp.GetRequiredService<IDataService>());
        collection.AddHostedService(sp => sp.GetRequiredService<INewsService>());
        collection.AddHostedService(sp => sp.GetRequiredService<IPlaytimeService>());
        collection.AddHostedService(sp => sp.GetRequiredService<ITocService>());
        collection.AddHostedService(sp => sp.GetRequiredService<IPacingService>());
        collection.AddHostedService(sp => sp.GetRequiredService<IQuestJournalService>());
        collection.AddHostedService(sp => sp.GetRequiredService<IQuestSnapshotService>());
        collection.AddHostedService(sp => sp.GetRequiredService<IQuestPatchService>());
        collection.AddHostedService(sp => sp.GetRequiredService<IWindowService>());
        collection.AddHostedService(sp => sp.GetRequiredService<ICommandService>());
      }).Build();
  }

  /// <summary>
  /// Dalamud calls this after construction. KamiToolKit has to be initialised
  /// before any addon exists, so that ordering is explicit here rather than
  /// left to whenever the container happens to resolve something.
  /// </summary>
  public async Task LoadAsync(CancellationToken cancellationToken)
  {
    KamiToolKitLibrary.Initialize(_pluginInterface, "Time Memoria");

    await _host.StartAsync(cancellationToken);

    _host.Services.GetRequiredService<INativeUiService>().Create();
  }

  private Configuration InitializeConfiguration(IServiceProvider s)
  {
    IDalamudPluginInterface pluginInterface = s.GetRequiredService<IDalamudPluginInterface>();
    Configuration configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
    configuration.Initialize(pluginInterface);
    return configuration;
  }

  /// <summary>
  /// Order here is not a preference, it is KamiToolKit's contract, and getting
  /// it wrong crashed the game during an earlier attempt:
  ///
  /// 1. Await each addon. Closing one plays an animation lasting several
  ///    frames, so the synchronous Dispose returns while the addon is still
  ///    alive. This await must not happen on the main thread — it waits on
  ///    frames, and the main thread is what advances them.
  /// 2. Then the library's own cleanup, which is the opposite: it silently
  ///    does nothing off the main thread, hence RunOnFrameworkThread.
  /// 3. Then the host, last, because it owns the services the addons read.
  /// </summary>
  public async ValueTask DisposeAsync()
  {
    await _host.Services.GetRequiredService<INativeUiService>().DisposeAsync();
    await _framework.RunOnFrameworkThread(KamiToolKitLibrary.Dispose);

    await _host.StopAsync();
    _host.Dispose();
  }
}
