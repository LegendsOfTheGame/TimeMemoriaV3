namespace TimeMemoria.Features;

public interface IFeatureService : IHostedService
{
  IReadOnlyList<Feature> Features { get; }

  /// <summary>Switches a feature on or off and saves the choice.</summary>
  void SetEnabled(Feature feature, bool enabled);

  /// <summary>Opens the enabled feature that answers to this <c>/tm</c>
  /// argument. False when no enabled feature does.</summary>
  bool TryOpen(string argument);
}

/// <summary>
/// Owns the optional features and their on/off state. Features are registered
/// in Plugin as <see cref="Feature"/> singletons, and this takes them all, so a
/// new feature needs one line there and nothing here.
///
/// A feature that throws while enabling is switched back off and logged, as
/// VanillaPlus does, so one broken feature cannot stop the plugin loading.
/// </summary>
public class FeatureService(ILogger _logger, IEnumerable<Feature> _features, Configuration _configuration,
  IFramework _framework) : IFeatureService
{
  private readonly List<Feature> _list = [.. _features.OrderBy((f) => f.DisplayName, StringComparer.Ordinal)];

  public IReadOnlyList<Feature> Features => _list;

  public async Task StartAsync(CancellationToken cancellationToken)
  {
    // Windows are added to the WindowSystem, which draws on the framework
    // thread, so enabling happens there too.
    await _framework.RunOnFrameworkThread(() =>
    {
      foreach (Feature feature in _list)
        if (_configuration.EnabledFeatures.Contains(feature.Key))
          TryEnable(feature);
    });

    await _logger.ServiceLifecycle();
  }

  public async Task StopAsync(CancellationToken cancellationToken)
  {
    // Not removed from the config: unloading the plugin is not the player
    // switching a feature off.
    await _framework.RunOnFrameworkThread(() =>
    {
      foreach (Feature feature in _list.Where((f) => f.IsEnabled))
        TryDisable(feature);
    });

    await _logger.ServiceLifecycle();
  }

  public void SetEnabled(Feature feature, bool enabled)
  {
    if (feature.IsEnabled == enabled) return;

    if (enabled) TryEnable(feature);
    else TryDisable(feature);

    bool changed = feature.IsEnabled
      ? _configuration.EnabledFeatures.Add(feature.Key)
      : _configuration.EnabledFeatures.Remove(feature.Key);

    if (changed) _configuration.Save();
  }

  public bool TryOpen(string argument)
  {
    Feature? feature = _list.FirstOrDefault((f) =>
      f.IsEnabled && string.Equals(f.CommandArgument, argument, StringComparison.OrdinalIgnoreCase));

    feature?.Open();
    return feature is not null;
  }

  private void TryEnable(Feature feature)
  {
    try
    {
      feature.OnEnable();
      feature.IsEnabled = true;
      _logger.Debug($"[Features] Enabled {feature.Key}.");
    }
    catch (Exception ex)
    {
      _logger.Error(ex, $"[Features] {feature.Key} failed to enable; switching it back off.");
      TryDisable(feature);
    }
  }

  private void TryDisable(Feature feature)
  {
    try
    {
      feature.OnDisable();
    }
    catch (Exception ex)
    {
      _logger.Error(ex, $"[Features] {feature.Key} failed to disable cleanly.");
    }

    feature.IsEnabled = false;
  }
}
