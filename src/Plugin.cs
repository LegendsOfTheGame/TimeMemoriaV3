using Microsoft.Extensions.Logging;
using ILogger = TimeMemoria.Services.ILogger;

namespace TimeMemoria;

public sealed class Plugin : IDalamudPlugin
{
  private readonly IHost _host;

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
    KamiToolKit.KamiToolKitLibrary.Initialize(pluginInterface, "Time Memoria");

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

        // Registered rather than constructed on demand so the host disposes it.
        // A native addon registers itself with the game by InternalName, so one
        // left undisposed collides with its replacement after a plugin reload.
        collection.AddSingleton(sp => new Windows.Native.ProgressionAddon
        {
          InternalName = "TMProgression",
          Title = "Class & Job Progression",
          Size = new System.Numerics.Vector2(420.0f, 500.0f),
          ProgressService = sp.GetRequiredService<IClassJobProgressService>()
        });
        collection.AddSingleton<ILogger, Logger>();
        collection.AddSingleton<IDataService, DataService>();
        collection.AddSingleton<IClassJobProgressService, ClassJobProgressService>();
        collection.AddSingleton<ILedgerExportService, LedgerExportService>();
        collection.AddSingleton<INewsService, NewsService>();
        collection.AddSingleton<IPlaytimeService, PlaytimeService>();
        collection.AddSingleton<ITocService, TocService>();
        collection.AddSingleton<IPacingService, PacingService>();
        collection.AddSingleton<IWindowService, WindowService>();
        collection.AddSingleton<ICommandService, CommandService>();

        collection.AddSingleton(InitializeConfiguration);
        collection.AddSingleton(new WindowSystem(pluginInterface.InternalName));

        collection.AddHostedService(sp => sp.GetRequiredService<IDataService>());
        collection.AddHostedService(sp => sp.GetRequiredService<INewsService>());
        collection.AddHostedService(sp => sp.GetRequiredService<IPlaytimeService>());
        collection.AddHostedService(sp => sp.GetRequiredService<ITocService>());
        collection.AddHostedService(sp => sp.GetRequiredService<IWindowService>());
        collection.AddHostedService(sp => sp.GetRequiredService<ICommandService>());
      }).Build();

    _host.StartAsync();
  }

  private Configuration InitializeConfiguration(IServiceProvider s)
  {
    IDalamudPluginInterface pluginInterface = s.GetRequiredService<IDalamudPluginInterface>();
    Configuration configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
    configuration.Initialize(pluginInterface);
    return configuration;
  }

  public void Dispose()
  {
    // Order matters. The host owns the native addon, and disposing that addon
    // frees game UI nodes -- so the library owning that infrastructure has to
    // outlive it. Tearing the library down first frees the nodes out from under
    // the addon, which takes the game process with it rather than throwing.
    _host.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    _host.Dispose();

    KamiToolKit.KamiToolKitLibrary.Dispose();
  }
}
