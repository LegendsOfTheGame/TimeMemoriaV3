namespace TimeMemoria.Services;

public interface ILedgerExportService
{
  /// <summary>Time Memoria's own progression shape.</summary>
  string BuildProgressionJson();

  /// <summary>The Adventurer's Ledger field shape, ready to merge.</summary>
  string BuildLedgerJson();
}

/// <summary>
/// Builds the clipboard payloads. Local only — output goes to the clipboard and
/// nowhere else; the plugin makes no outbound requests.
/// </summary>
public class LedgerExportService(IPluginLog _pluginLog, IPlayerState _playerState, IClassJobProgressService _classJobProgress)
  : ILedgerExportService
{
  public string BuildProgressionJson()
  {
    try
    {
      JsonArray jobs = [];
      foreach (ClassJobProgress p in _classJobProgress.GetProgress().Where(p => p.IsUnlocked))
      {
        jobs.Add(new JsonObject
        {
          ["name"] = p.Name,
          ["abbreviation"] = p.Abbreviation,
          ["category"] = p.Category,
          ["level"] = p.Level,
          ["exp"] = p.Experience,
          ["expToNext"] = p.ExperienceToNext
        });
      }

      JsonObject root = new()
      {
        ["character"] = _playerState.IsLoaded ? _playerState.CharacterName : string.Empty,
        ["world"] = WorldName(),
        ["exportedUtc"] = DateTime.UtcNow.ToString("o"),
        ["classJobs"] = jobs
      };

      return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    catch (Exception ex)
    {
      _pluginLog.Error(ex, "[LedgerExport] Failed to build progression payload");
      return "{}";
    }
  }

  public string BuildLedgerJson()
  {
    try
    {
      JsonObject root = new()
      {
        ["source"] = "time-memoria",
        ["version"] = typeof(LedgerExportService).Assembly.GetName().Version?.ToString() ?? "unknown",
        ["exported"] = DateTime.UtcNow.ToString("o"),
        ["name"] = _playerState.IsLoaded ? _playerState.CharacterName : string.Empty,
        ["server"] = BuildServer()
      };

      if (_playerState.IsLoaded) root["comm"] = _playerState.PlayerCommendations;

      List<ClassJobProgress> progress = _classJobProgress.GetProgress();
      root["combat"] = BuildLevels(progress, "combat");
      root["craft"] = BuildLevels(progress, "craft");
      root["gather"] = BuildLevels(progress, "gather");

      return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    catch (Exception ex)
    {
      _pluginLog.Error(ex, "[LedgerExport] Failed to build ledger payload");
      return "{}";
    }
  }

  /// <summary>
  /// Levels the way the ledger stores them: level plus the fraction of the way
  /// through it, to three decimals. Max level emits a bare integer.
  /// </summary>
  private static JsonObject BuildLevels(List<ClassJobProgress> progress, string category)
  {
    JsonObject obj = [];

    foreach (ClassJobProgress job in progress.Where(p => p.Category == category))
    {
      if (job.Level == 0 || job.IsMaxLevel) obj[job.Name] = job.Level;
      else obj[job.Name] = Math.Round(job.Level + job.Fraction, 3, MidpointRounding.AwayFromZero);
    }

    return obj;
  }

  private JsonObject BuildServer()
  {
    JsonObject server = new() { ["pdc"] = "", ["ldc"] = "", ["world"] = "" };
    if (!_playerState.IsLoaded) return server;

    World? world = _playerState.HomeWorld.ValueNullable;
    if (world == null) return server;

    server["world"] = world.Value.Name.ToString();

    // Home world -> data centre -> region group, which is what the ledger calls
    // the physical data centre.
    WorldDCGroupType? dc = world.Value.DataCenter.ValueNullable;
    if (dc == null) return server;

    server["ldc"] = dc.Value.Name.ToString();
    WorldRegionGroup? region = dc.Value.Region.ValueNullable;
    if (region != null) server["pdc"] = region.Value.Name.ToString();

    return server;
  }

  private string WorldName()
  {
    if (!_playerState.IsLoaded) return string.Empty;
    return _playerState.HomeWorld.ValueNullable?.Name.ToString() ?? string.Empty;
  }
}
