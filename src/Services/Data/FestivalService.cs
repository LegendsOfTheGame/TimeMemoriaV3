namespace TimeMemoria.Services;

/// <summary>
/// A festival the client currently has switched on.
/// </summary>
/// <param name="Id">Festival sheet row id.</param>
/// <param name="Name">Resolved name, blank when the id is in neither source.</param>
/// <param name="Phase">Where the festival is in its run. Meaning varies by event.</param>
public record ActiveFestival(uint Id, string Name, ushort Phase)
{
  public string DisplayName => Name.Length > 0 ? Name : $"Festival #{Id}";
}

public interface IFestivalService : IHostedService
{
  /// <summary>Festivals the game reports as running right now.</summary>
  List<ActiveFestival> GetActive();

  /// <summary>
  /// Whether this install has ever observed <paramref name="festivalId"/> in
  /// <see cref="GetActive"/>'s result, this session or an earlier one.
  ///
  /// Deliberately a set rather than a high-water mark — see
  /// <c>festival-gate-design.md</c> for why assuming festival ids are allocated
  /// in event order is not safe to build on.
  /// </summary>
  bool WasEverActive(uint festivalId);
}

/// <summary>
/// Reads active festivals straight from the client.
///
/// The news feed can only report events someone remembered to teach it about —
/// it matches article titles against a list of known seasonal names, so a
/// collaboration nobody anticipated is invisible to it. The client has no such
/// problem: a festival is either switched on or it is not.
///
/// The trade is that the game knows an event is running and roughly where it is
/// in its run, but not when it ends. Dates still have to come from the feed.
///
/// The Festival sheet ships with every Name blank, so the names come from
/// festival-names.json instead — a copy of the crowd-sourced list in
/// Critical-Impact/LuminaSupplemental, which is GPL-3.0.
///
/// Also remembers every festival id this install has ever seen active, for
/// <see cref="WasEverActive"/>. Global rather than per-character — which
/// festival is running is server-wide state, true for every character on the
/// account alike, not something that belongs to one of them.
/// </summary>
public class FestivalService(
  ILogger _logger,
  IDataManager _dataManager,
  IDalamudPluginInterface _pluginInterface,
  Configuration _configuration,
  IFramework _framework) : IFestivalService
{
  private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

  private readonly Dictionary<uint, string> _names = LoadNames(_logger, _pluginInterface);
  private DateTime _lastPollUtc = DateTime.MinValue;

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _framework.Update += OnFrameworkUpdate;
    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _framework.Update -= OnFrameworkUpdate;
    return _logger.ServiceLifecycle();
  }

  public bool WasEverActive(uint festivalId) => _configuration.SeenActiveFestivalIds.Contains(festivalId);

  /// <summary>
  /// Folds whatever is active right now into the seen set. Cheap enough to run
  /// on every call to <see cref="GetActive"/> as well, but polled independently
  /// so a festival is recorded even in a session where nothing ever draws the
  /// windows that call it.
  /// </summary>
  private void OnFrameworkUpdate(IFramework framework)
  {
    DateTime now = DateTime.UtcNow;
    if (now - _lastPollUtc < PollInterval) return;
    _lastPollUtc = now;

    Remember(GetActive());
  }

  private void Remember(List<ActiveFestival> active)
  {
    if (active.Count == 0) return;

    bool changed = false;

    foreach (ActiveFestival festival in active)
      changed |= _configuration.SeenActiveFestivalIds.Add(festival.Id);

    if (!changed) return;

    _configuration.Save();
    _logger.Debug($"[Festival] Seen set now {_configuration.SeenActiveFestivalIds.Count} id(s).");
  }

  private static Dictionary<uint, string> LoadNames(ILogger logger, IDalamudPluginInterface pluginInterface)
  {
    Dictionary<uint, string> names = [];

    try
    {
      string path = Path.Combine(pluginInterface.AssemblyLocation.Directory!.FullName, "data", "festival-names.json");
      if (!File.Exists(path))
      {
        logger.Error($"[Festival] festival-names.json not found at {path}; falling back to ids.");
        return names;
      }

      Dictionary<string, string> raw =
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];

      foreach (KeyValuePair<string, string> pair in raw)
        if (uint.TryParse(pair.Key, out uint id))
          names[id] = pair.Value;

      logger.Debug($"[Festival] Loaded {names.Count} festival names.");
    }
    catch (Exception ex)
    {
      logger.Error(ex, "[Festival] Failed to load festival-names.json; falling back to ids.");
    }

    return names;
  }

  /// <summary>
  /// True for rows the client switches on alongside a real event rather than
  /// as one.
  ///
  /// "Special Event Flag" marks that a recurring event is running; the event
  /// itself is switched on separately and named properly. Both being active at
  /// once is normal, so showing them both lists the same thing twice, once
  /// under a name that means nothing to a player.
  /// </summary>
  private bool IsInternalFlag(uint id)
    => _names.TryGetValue(id, out string? name) && name == "Special Event Flag";

  public unsafe List<ActiveFestival> GetActive()
  {
    List<ActiveFestival> result = [];

    try
    {
      FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState* state =
        FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();

      if (state is null || !state->IsLoaded) return result;

      Lumina.Excel.ExcelSheet<Festival> sheet = _dataManager.GetExcelSheet<Festival>();

      Span<ushort> ids = state->ActiveFestivalIds;
      Span<ushort> phases = state->ActiveFestivalPhases;

      for (int i = 0; i < ids.Length; i++)
      {
        if (ids[i] == 0) continue;
        if (IsInternalFlag(ids[i])) continue;

        // The mapped name wins: it carries the year, so repeat events like
        // All Saints' Wake stay distinguishable. The sheet is only a fallback
        // for ids the list has not caught up with.
        string name = _names.TryGetValue(ids[i], out string? mapped)
          ? mapped
          : sheet.GetRowOrDefault(ids[i])?.Name.ToString() ?? "";

        ushort phase = i < phases.Length ? phases[i] : (ushort)0;

        result.Add(new ActiveFestival(ids[i], name, phase));
      }
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "[Festival] Failed to read active festivals");
    }

    Remember(result);
    return result;
  }
}
