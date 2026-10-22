using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using XBMManager = FFXIVClientStructs.FFXIV.Client.Game.XBMManager;

namespace TimeMemoria.Features.Bestiary;

/// <summary>
/// The Master's Bestiary table: every Beastmaster beast, the enemy it is tamed
/// from, where, the minimum level, the Kornago Gourd that unlocks it, and
/// whether this character has captured it. The same function as the separate
/// Bestiary Mapper plugin (<c>/bmap</c>), so a player who turns this on does
/// not need that plugin installed.
///
/// The flag button opens the native map at a beast's taming spot. That is the
/// map-open carve-out of the Prime Directive, extended to beast locations by
/// the user on 20 October 2026.
/// </summary>
public unsafe class BestiaryFeature(ILogger _logger, WindowSystem _windowSystem, IDataManager _dataManager,
  IGameGui _gameGui, IDalamudPluginInterface _pluginInterface) : Feature
{
  public override string Key => "Bestiary";
  public override string DisplayName => "Master's Bestiary";
  public override string Description =>
    "A table of every Beastmaster beast: where to tame it, the minimum level, the Kornago Gourd, " +
    "and whether you have captured it. Click a flag to open the map there.";
  public override string? CommandArgument => "bestiary";

  /// <summary>
  /// Bestiary Mapper's short command, kept so that its players need nothing
  /// new. Bestiary Mapper retires (user's decision, 22 Oct 2026). Not
  /// /bestiary: the game already uses it to open its own Master's Bestiary,
  /// and a plugin command of that name would hide the game's.
  /// </summary>
  public override string[] SlashCommands => ["/bmap"];

  private BestiaryWindow? _window;

  /// <summary>Zone name to the TerritoryType and Map rows a map link needs.</summary>
  private readonly Dictionary<string, (uint TerritoryId, uint MapId)?> _zones = new(StringComparer.OrdinalIgnoreCase);

  protected internal override void OnEnable()
  {
    _window ??= new BestiaryWindow(this, LoadBeasts());
    if (!_windowSystem.Windows.Contains(_window)) _windowSystem.AddWindow(_window);
  }

  protected internal override void OnDisable()
  {
    if (_window is null) return;

    _window.IsOpen = false;
    if (_windowSystem.Windows.Contains(_window)) _windowSystem.RemoveWindow(_window);
  }

  public override void Open()
  {
    if (_window is not null) _window.IsOpen = true;
  }

  /// <summary>
  /// /bmap opens the game's own Master's Bestiary beside this table. Approved
  /// as a Prime Directive amendment on 22 Oct 2026, for the command only: the
  /// Settings Open button and /tm bestiary open this table alone.
  ///
  /// MemoriaProbe's "bstopen" probe confirmed it on 22 Oct 2026: Show() on the
  /// XBMMonsterNotebook agent took IsAgentActive from false to true and opened
  /// the window. Not called when the window is already open, so that a second
  /// /bmap cannot close it.
  /// </summary>
  public override void OnSlashCommand()
  {
    Open();

    AgentModule* module = AgentModule.Instance();
    if (module is null) return;

    AgentInterface* agent = module->GetAgentByInternalId(AgentId.XBMMonsterNotebook);
    if (agent is not null && !agent->IsAgentActive()) agent->Show();
  }

  /// <summary>
  /// Whether the client has the capture list yet. Until it does, every beast
  /// reads as not captured, which would be wrong, so the window says so instead.
  /// </summary>
  public bool CaptureStateReady
  {
    get
    {
      XBMManager* xbm = XBMManager.Instance();
      return xbm is not null && xbm->State == XBMManager.DataState.Received;
    }
  }

  /// <summary>
  /// Read live, never stored. Beast.No is the petId the game itself uses:
  /// MemoriaProbe's "bstpets" probe matched NumUnlockedPets and every row to
  /// the Master's Bestiary window.
  /// </summary>
  public bool IsCaptured(Beast beast)
  {
    XBMManager* xbm = XBMManager.Instance();
    return xbm is not null && xbm->State == XBMManager.DataState.Received && xbm->IsPetUnlocked((uint)beast.No);
  }

  public bool OpenOnMap(Beast beast)
  {
    if (!beast.HasMapLocation) return false;
    if (ResolveZone(beast.Zone!) is not (uint territoryId, uint mapId)) return false;

    _gameGui.OpenMapWithMapLink(new MapLinkPayload(territoryId, mapId, beast.X!.Value, beast.Y!.Value));
    return true;
  }

  /// <summary>
  /// By PlaceName against the TerritoryType sheet, as Bestiary Mapper does,
  /// rather than a table of ids, so it survives patches. A miss is cached too,
  /// so a bad zone name is logged once and not on every click.
  /// </summary>
  private (uint, uint)? ResolveZone(string zone)
  {
    if (_zones.TryGetValue(zone, out (uint, uint)? cached)) return cached;

    (uint, uint)? found = null;
    foreach (TerritoryType territory in _dataManager.GetExcelSheet<TerritoryType>())
    {
      if (territory.PlaceName.ValueNullable is not PlaceName place) continue;
      if (!string.Equals(place.Name.ToString(), zone, StringComparison.OrdinalIgnoreCase)) continue;
      if (territory.Map.ValueNullable is not Map map) continue;

      found = (territory.RowId, map.RowId);
      break;
    }

    if (found is null) _logger.Error($"[Bestiary] Zone '{zone}' matches no TerritoryType.");
    _zones[zone] = found;
    return found;
  }

  private List<Beast> LoadBeasts()
  {
    string path = Path.Combine(_pluginInterface.AssemblyLocation.Directory!.FullName, "data", "bestiary.json");
    List<Beast> beasts = JsonSerializer.Deserialize<List<Beast>>(File.ReadAllText(path)) ?? [];
    _logger.Debug($"[Bestiary] Loaded {beasts.Count} beasts.");
    return beasts;
  }
}
