namespace TimeMemoria.Services;

public interface IQuestPatchService : IHostedService
{
  /// <summary>
  /// The patch that introduced a quest, formatted for display, or null when the
  /// map has nothing for any of its ids.
  /// </summary>
  string? GetPatch(IReadOnlyList<uint> ids);

  /// <summary>Same value as a number, for sorting and comparison.</summary>
  decimal? GetPatchValue(IReadOnlyList<uint> ids);

  /// <summary>How many ids the map holds. Zero means it failed to load.</summary>
  int Count { get; }
}

/// <summary>
/// Which patch each quest arrived in.
///
/// No such data exists in the game files — there is no patch sheet, only
/// ExVersion with its six expansion rows, so the client can say a quest is
/// Endwalker but not that it is 6.3. This map was assembled from Garland Tools,
/// which derives it by diffing XIVAPI dumps across versions.
/// </summary>
public class QuestPatchService(ILogger _logger, IDalamudPluginInterface _pluginInterface) : IQuestPatchService
{
  private readonly Dictionary<uint, decimal> _patches = [];

  public int Count => _patches.Count;

  public Task StartAsync(CancellationToken cancellationToken)
  {
    try
    {
      string path = Path.Combine(_pluginInterface.AssemblyLocation.Directory!.FullName, "data", "quest-patches.json");
      if (!File.Exists(path))
      {
        _logger.Error($"[QuestPatch] quest-patches.json not found at {path}; patch numbers hidden.");
        return _logger.ServiceLifecycle();
      }

      Dictionary<string, decimal?> raw =
        JsonSerializer.Deserialize<Dictionary<string, decimal?>>(File.ReadAllText(path)) ?? [];

      foreach (KeyValuePair<string, decimal?> pair in raw)
        if (pair.Value.HasValue && uint.TryParse(pair.Key, out uint id))
          _patches[id] = pair.Value.Value;

      _logger.Debug($"[QuestPatch] Loaded {_patches.Count} quest patches.");
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "[QuestPatch] Failed to load quest-patches.json; patch numbers hidden.");
    }

    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken) => _logger.ServiceLifecycle();

  /// <summary>
  /// Lowest patch across a quest's ids.
  ///
  /// A quest replaced by a later patch keeps every id it has ever had, and they
  /// disagree: Way of the Gladiator is 65821 = 2.0 and 65789 = 3.1, because 3.1
  /// added a second route into content that shipped at launch. The question
  /// being asked is when the content first existed, so the earliest wins.
  ///
  /// Note that id order does not track patch order — 65789 is both the lower id
  /// and the later patch — so this cannot be shortcut by taking the first id.
  /// </summary>
  public decimal? GetPatchValue(IReadOnlyList<uint> ids)
  {
    decimal? lowest = null;

    foreach (uint id in ids)
      if (_patches.TryGetValue(id, out decimal patch) && (lowest is null || patch < lowest))
        lowest = patch;

    return lowest;
  }

  public string? GetPatch(IReadOnlyList<uint> ids)
  {
    decimal? value = GetPatchValue(ids);

    // "2.0" and "6.55" both matter -- one decimal is the norm, two for the
    // interim patches, and neither should gain or lose a digit.
    return value?.ToString("0.0##", CultureInfo.InvariantCulture);
  }
}
