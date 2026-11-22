using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace TimeMemoria.Features.HuntingLog;

/// <summary>
/// The Hunting Log for every class at once: each monster that is still needed
/// in the current rank of each unlocked class, and of the player's Grand
/// Company, with where to find it.
///
/// The game's own Hunting Log window and the Hunty plugin both show one class
/// at a time and follow the current job. The client does not need that:
/// MemoriaProbe's "huntlog" probe showed on 22 Nov 2026 that MonsterNoteManager
/// holds the kill counts of all twelve logs from login, with the window never
/// opened, and that a class change does not change them. The counts matched
/// the game's window monster by monster (Marauder rank 4).
///
/// Read live, never stored. The flag button opens the native map at a
/// monster's location: the map-open carve-out of the Prime Directive, extended
/// to Hunting Log monsters by the user on 22 Nov 2026 as part of the beast
/// taming amendment.
/// </summary>
public unsafe class HuntingLogFeature(ILogger _logger, WindowSystem _windowSystem, IDataManager _dataManager,
  IGameGui _gameGui, IClientState _clientState, IPlayerState _playerState,
  IDalamudPluginInterface _pluginInterface) : Feature
{
  public override string Key => "HuntingLog";
  public override string DisplayName => "Hunting Log";
  public override string Description =>
    "Every Hunting Log monster you still need, for all your classes and your Grand Company at once, " +
    "with where to find it. Click a flag to open the map there.";
  public override string? CommandArgument => "huntlog";
  public override string[] SlashCommands => ["/hunt"];

  /// <summary>
  /// MonsterNoteManager block order, from Hunty's Data.cs StaticData.JobInMemory
  /// (Infiziert90/Hunty): ClassJob row for blocks 0-7 and 11, Grand Company row
  /// for blocks 8-10 (stored here as negative numbers to keep one table).
  /// </summary>
  private static readonly int[] BlockOwner = [1, 2, 3, 4, 5, 6, 7, 26, -1, -2, -3, 29];

  private const int ClassRanks = 5;
  private const int CompanyRanks = 3;
  private const int TasksPerRank = 10;
  private const int MonstersPerTask = 4;

  private HuntingLogWindow? _window;

  /// <summary>MonsterNote row id to the locations of each of its monster slots.</summary>
  private Dictionary<uint, List<List<HuntLocation>>>? _locations;

  /// <summary>TerritoryType id to its display name, filled on first use.</summary>
  private readonly Dictionary<uint, string> _placeNames = [];

  /// <summary>
  /// The job from the last ClassJobChanged event, as Hunty does it. Haven saw
  /// on 22 Nov 2026 that the window kept the old class after a class change
  /// until it was closed and opened again; the event names the new job at
  /// once. Null until the first change after login.
  /// </summary>
  private uint? _eventJob;

  protected internal override void OnEnable()
  {
    _locations ??= LoadLocations();
    _window ??= new HuntingLogWindow(this);
    if (!_windowSystem.Windows.Contains(_window)) _windowSystem.AddWindow(_window);

    _clientState.ClassJobChanged += OnClassJobChanged;
    _clientState.TerritoryChanged += OnTerritoryChanged;
    _clientState.Logout += OnLogout;
  }

  protected internal override void OnDisable()
  {
    _clientState.ClassJobChanged -= OnClassJobChanged;
    _clientState.TerritoryChanged -= OnTerritoryChanged;
    _clientState.Logout -= OnLogout;

    if (_window is null) return;

    _window.IsOpen = false;
    if (_windowSystem.Windows.Contains(_window)) _windowSystem.RemoveWindow(_window);
  }

  public override void Open()
  {
    if (_window is not null) _window.IsOpen = true;
  }

  public bool IsLoggedIn => _playerState.IsLoaded;

  private void OnClassJobChanged(uint classJobId)
  {
    _eventJob = classJobId;
    _window?.RequestRefresh();
  }

  /// <summary>"Current zone only" and the preferred location both follow the zone.</summary>
  private void OnTerritoryChanged(uint territoryId) => _window?.RequestRefresh();

  private void OnLogout(int type, int code) => _eventJob = null;

  /// <summary>
  /// The current state of every log the player can work on. Classes that are
  /// not unlocked (level 0) are left out, and of the three Grand Company logs
  /// only the player's own is shown: kills count only in that one.
  /// </summary>
  public List<HuntLogState> ReadLogs()
  {
    List<HuntLogState> logs = [];
    MonsterNoteManager* notes = MonsterNoteManager.Instance();
    if (notes is null || !_playerState.IsLoaded) return logs;

    uint company = _playerState.GrandCompany.RowId;
    uint jobId = _eventJob ?? _playerState.ClassJob.RowId;
    uint currentClass = _dataManager.GetExcelSheet<ClassJob>().GetRowOrDefault(jobId)?.ClassJobParent.RowId ?? 0;

    for (int block = 0; block < BlockOwner.Length; block++)
    {
      int owner = BlockOwner[block];
      bool isCompany = owner < 0;
      string name;
      uint sheetBase;

      if (isCompany)
      {
        if (company != (uint)-owner) continue;
        name = TitleCase(_dataManager.GetExcelSheet<GrandCompany>().GetRowOrDefault(company)?.Name.ToString() ?? $"Grand Company {company}");
        sheetBase = company * 1_000_000;
      }
      else
      {
        if (_dataManager.GetExcelSheet<ClassJob>().GetRowOrDefault((uint)owner) is not ClassJob job) continue;
        if (_playerState.GetClassJobLevel(job) <= 0) continue;
        name = TitleCase(job.Name.ToString());
        sheetBase = (uint)owner * 10_000;
      }

      MonsterNoteRankInfo info = notes->RankData[block];
      int rankCount = isCompany ? CompanyRanks : ClassRanks;
      HuntLogState log = new()
      {
        Name = name,
        ClassId = isCompany ? 0 : (uint)owner,
        Order = block,
        IsCurrent = isCompany || (uint)owner == currentClass,
        Rank = info.Rank,
        RankCount = rankCount,
      };
      logs.Add(log);

      if (info.Rank >= rankCount) continue;

      for (int task = 0; task < TasksPerRank; task++)
      {
        uint rowId = sheetBase + (uint)info.Rank * 10 + (uint)task + 1;
        if (_dataManager.GetExcelSheet<MonsterNote>().GetRowOrDefault(rowId) is not MonsterNote note) continue;

        bool taskDone = true;
        bool taskUsed = false;
        for (int slot = 0; slot < MonstersPerTask; slot++)
        {
          int needed = note.Count[slot];
          if (needed <= 0 || note.MonsterNoteTarget[slot].ValueNullable is not MonsterNoteTarget target) continue;
          taskUsed = true;

          int killed = info.RankData[task].Counts[slot];
          if (killed >= needed) continue;
          taskDone = false;

          log.Needed.Add(new HuntTarget
          {
            Log = log,
            Task = note.Name.ToString(),
            TaskOrder = task,
            Monster = TitleCase(target.BNpcName.ValueNullable?.Singular.ToString() ?? "?"),
            Killed = killed,
            Needed = needed,
            Locations = LocationsFor(rowId, slot),
          });
        }

        if (taskUsed && taskDone) log.TasksDone++;
        if (taskUsed) log.TasksInRank++;
      }
    }

    return logs;
  }

  /// <summary>
  /// The location to show and flag: one in the zone the player stands in when
  /// there is one, else the first that Hunty lists.
  /// </summary>
  public HuntLocation? PreferredLocation(HuntTarget target)
  {
    if (target.Locations.Count == 0) return null;
    return target.Locations.FirstOrDefault(IsHere) ?? target.Locations[0];
  }

  /// <summary>Whether the monster has a location in the zone the player is in.</summary>
  public bool IsInCurrentZone(HuntTarget target) => target.Locations.Any(IsHere);

  /// <summary>
  /// The open-world zone, or the duty. Inside a duty the client reports the
  /// duty's TerritoryType, which the data holds as the third value.
  /// </summary>
  private bool IsHere(HuntLocation location)
  {
    uint here = _clientState.TerritoryType;
    return location.TerritoryId == here || (location.DutyTerritoryId != 0 && location.DutyTerritoryId == here);
  }

  public string Describe(HuntLocation location)
  {
    string zone = PlaceName(location.TerritoryId);
    string spot = $"{zone} ({location.X:0.#}, {location.Y:0.#})";
    return location.DutyTerritoryId == 0 ? spot : $"{DutyName(location.DutyTerritoryId)}: {spot}";
  }

  /// <summary>
  /// The player's gear sets whose job belongs to this base class: for
  /// Pugilist, any Pugilist or Monk set. Read from the player's own list on
  /// every call, never by set number, because every player numbers sets
  /// differently. MemoriaProbe's "gearset" probe read the list on 22 Nov 2026
  /// and matched the game's Gear Set window (number = Id + 1).
  /// </summary>
  public List<HuntGearset> GearsetsFor(uint classId)
  {
    List<HuntGearset> sets = [];
    if (classId == 0) return sets;

    RaptureGearsetModule* module = RaptureGearsetModule.Instance();
    if (module is null) return sets;

    var jobs = _dataManager.GetExcelSheet<ClassJob>();
    for (int id = 0; id < 100; id++)
    {
      if (!module->IsValidGearset(id)) continue;
      RaptureGearsetModule.GearsetEntry* entry = module->GetGearset(id);
      if (entry is null) continue;
      if (jobs.GetRowOrDefault(entry->ClassJob)?.ClassJobParent.RowId != classId) continue;

      string job = jobs.GetRowOrDefault(entry->ClassJob)?.Abbreviation.ToString() ?? "";
      sets.Add(new HuntGearset(id, $"{id + 1}: {entry->NameString} ({job})"));
    }
    return sets;
  }

  /// <summary>
  /// Prime Directive amendment of 22 Nov 2026: a click on a class name in the
  /// summary line changes to that gear set. The game's own function, the same
  /// as a click in the Gear Set window; nothing goes through chat (XIVDeck's
  /// "/gearset change" does, and that path stays banned). One click, one
  /// change. When the game refuses (combat, duty, casting) it shows its own
  /// message; the plugin adds nothing. Tested in MemoriaProbe ("gearset") on
  /// 22 Nov 2026: set 19 equipped, CurrentGearsetIndex 14 → 18.
  /// </summary>
  public void Equip(HuntGearset set)
  {
    RaptureGearsetModule* module = RaptureGearsetModule.Instance();
    if (module is null || !module->IsValidGearset(set.Id)) return;

    int result = module->EquipGearset(set.Id);
    _logger.Debug($"[HuntingLog] EquipGearset({set.Id}) returned {result}.");
  }

  public void OpenOnMap(HuntLocation location)
  {
    _gameGui.OpenMapWithMapLink(new MapLinkPayload(location.TerritoryId, location.MapId, location.X, location.Y));
  }

  private string PlaceName(uint territoryId)
  {
    if (_placeNames.TryGetValue(territoryId, out string? cached)) return cached;
    string name = _dataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId)?.PlaceName.ValueNullable?.Name.ToString() ?? "";
    if (name.Length == 0) name = $"Zone {territoryId}";
    _placeNames[territoryId] = name;
    return name;
  }

  private string DutyName(uint territoryId)
  {
    string name = _dataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(territoryId)?.ContentFinderCondition.ValueNullable?.Name.ToString() ?? "";
    return name.Length > 0 ? TitleCase(name) : PlaceName(territoryId);
  }

  private List<HuntLocation> LocationsFor(uint rowId, int slot)
  {
    if (_locations is null || !_locations.TryGetValue(rowId, out List<List<HuntLocation>>? slots)) return [];
    return slot < slots.Count ? slots[slot] : [];
  }

  /// <summary>
  /// English sheet names are lower case ("grass raptor"); the game's own window
  /// capitalises them. Other languages are left as the sheet gives them.
  /// </summary>
  private string TitleCase(string text)
  {
    if (_clientState.ClientLanguage != ClientLanguage.English || text.Length == 0) return text;
    return CultureInfo.GetCultureInfo("en-US").TextInfo.ToTitleCase(text);
  }

  /// <summary>
  /// data/hunting-log.json: MonsterNote row id to one list of locations per
  /// monster slot, each location [TerritoryType, Map, duty TerritoryType, X, Y].
  /// A missing or bad file leaves every monster without a location, not the
  /// window without rows.
  /// </summary>
  private Dictionary<uint, List<List<HuntLocation>>> LoadLocations()
  {
    Dictionary<uint, List<List<HuntLocation>>> result = [];
    try
    {
      string path = Path.Combine(_pluginInterface.AssemblyLocation.Directory!.FullName, "data", "hunting-log.json");
      JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
      foreach ((string key, JsonNode? slots) in root)
      {
        if (!uint.TryParse(key, out uint rowId) || slots is not JsonArray slotArray) continue;
        result[rowId] = [.. slotArray.Select(slot => (slot as JsonArray ?? []).Select(l => new HuntLocation(
          (uint)l![0]!.GetValue<int>(), (uint)l[1]!.GetValue<int>(), (uint)l[2]!.GetValue<int>(),
          l[3]!.GetValue<float>(), l[4]!.GetValue<float>())).ToList())];
      }
      _logger.Debug($"[HuntingLog] Loaded locations for {result.Count} tasks.");
    }
    catch (Exception ex)
    {
      _logger.Error($"[HuntingLog] Could not load data/hunting-log.json: {ex.Message}");
    }
    return result;
  }
}

/// <summary>One class or Grand Company log, as the client holds it now.</summary>
public class HuntLogState
{
  public string Name { get; init; } = "";

  /// <summary>The ClassJob row of the base class; 0 for a Grand Company log.</summary>
  public uint ClassId { get; init; }

  /// <summary>MonsterNoteManager block index: the window lists logs in this order.</summary>
  public int Order { get; init; }

  /// <summary>The class of the current job, or the player's Grand Company.</summary>
  public bool IsCurrent { get; init; }

  /// <summary>Ranks complete. Equal to <see cref="RankCount"/> when the log is finished.</summary>
  public int Rank { get; init; }

  public int RankCount { get; init; }

  public int TasksDone { get; set; }

  public int TasksInRank { get; set; }

  public bool IsComplete => Rank >= RankCount;

  public List<HuntTarget> Needed { get; } = [];
}

/// <summary>A monster of the current rank that still needs kills.</summary>
public class HuntTarget
{
  public required HuntLogState Log { get; init; }

  /// <summary>The task's name in the game, such as "Marauder 32".</summary>
  public string Task { get; init; } = "";

  public int TaskOrder { get; init; }

  public string Monster { get; init; } = "";

  public int Killed { get; init; }

  public int Needed { get; init; }

  public List<HuntLocation> Locations { get; init; } = [];
}

/// <summary>A gear set by its Id (the Gear Set window shows Id + 1).</summary>
public record HuntGearset(int Id, string Label);

public record HuntLocation(uint TerritoryId, uint MapId, uint DutyTerritoryId, float X, float Y);
