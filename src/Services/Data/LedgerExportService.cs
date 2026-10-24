namespace TimeMemoria.Services;

public interface ILedgerExportService
{
  /// <summary>The Adventurer's Ledger field shape, ready to merge.</summary>
  string BuildLedgerJson();

  /// <summary>Collection unlocks, as a payload of its own. See BuildCollectionsJson.</summary>
  string BuildCollectionsJson();

  /// <summary>Both in one paste. See BuildFullJson.</summary>
  string BuildFullJson();
}

/// <summary>
/// Builds the clipboard payloads. Local only — output goes to the clipboard and
/// to the export history on disk, nowhere else; the plugin makes no outbound
/// requests.
/// </summary>
public class LedgerExportService(IPluginLog _pluginLog, IPlayerState _playerState, IClassJobProgressService _classJobProgress, IPlaytimeService _playtime, IDataService _dataService, ITocService _tocService, IAlliedSocietyService _societies, IAchievementService _achievements, ICosmicToolProgressService _cosmicTools, IDataManager _dataManager, IExportHistoryService _history)
  : ILedgerExportService
{
  /// <summary>Master books per crafter, built once: the sheets do not change in a session.</summary>
  private List<(string Job, List<uint> Numbered, List<uint> Others)>? _masterBooks;

  // Each public build is kept once. The full export builds the other two
  // privately, so it is not kept three times.
  public string BuildLedgerJson() => _history.Keep("ledger", LedgerJson());
  public string BuildCollectionsJson() => _history.Keep("collections", CollectionsJson());
  public string BuildFullJson() => _history.Keep("full", FullJson());

  private string LedgerJson()
  {
    try
    {
      // Forced: an export is a figure someone is about to paste somewhere and
      // keep. Being a few seconds stale is fine on screen and not fine here —
      // an export written into the ledger is wrong until the next one.
      _dataService.UpdateQuestData(true);

      // fmt 2 is the compact shape: job abbreviations, quest ids, arrays in
      // place of repeated keys, 1/0 for flags. The ledger and the Eorzea Line
      // convert it back to the fmt 1 shape on import; an export without "fmt"
      // is fmt 1.
      JsonObject root = new()
      {
        ["source"] = "time-memoria",
        ["fmt"] = 2,
        ["version"] = typeof(LedgerExportService).Assembly.GetName().Version?.ToString() ?? "unknown",
        ["exported"] = Stamp(DateTime.UtcNow),
        ["name"] = _playerState.IsLoaded ? _playerState.CharacterName : string.Empty,
        ["server"] = BuildServer()
      };

      if (_playerState.IsLoaded) root["comm"] = _playerState.PlayerCommendations;

      JsonObject? playtime = BuildPlaytime();
      if (playtime != null) root["playtime"] = playtime;

      List<ClassJobProgress> progress = _classJobProgress.GetProgress();
      root["combat"] = BuildLevels(progress, "combat");
      root["craft"] = BuildLevels(progress, "craft");
      root["gather"] = BuildLevels(progress, "gather");
      root["quests"] = BuildQuests();
      root["societies"] = BuildSocieties();
      root["classQuests"] = BuildClassQuests();

      JsonObject? collectables = BuildCollectables();
      if (collectables != null) root["collectables"] = collectables;

      JsonObject? duties = Reading(_achievements.Duties);
      if (duties != null) root["duties"] = duties;

      root["msqBreakdown"] = BuildMsqBreakdown();

      JsonObject? msqPatch = BuildMsqPatch();
      if (msqPatch != null) root["msqPatch"] = msqPatch;

      JsonObject? unlocks = BuildUnlocks();
      if (unlocks != null) root["unlocks"] = unlocks;

      JsonObject? cosmicTools = BuildCosmicTools(progress);
      if (cosmicTools != null) root["cosmicTools"] = cosmicTools;

      JsonObject? masterBooks = BuildMasterBooks();
      if (masterBooks != null) root["masterBooks"] = masterBooks;

      // Compact, for the clipboard and for pasting into an AI: the readers use
      // JSON.parse, so indentation is only size. The relaxed encoder keeps an
      // apostrophe as one character instead of a six-character escape; safe
      // because this text is never placed in HTML unparsed.
      return root.ToJsonString(new JsonSerializerOptions
      {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
      });
    }
    catch (Exception ex)
    {
      _pluginLog.Error(ex, "[LedgerExport] Failed to build ledger payload");
      return "{}";
    }
  }

  /// <summary>
  /// Collections: what this character owns, by sheet row id, one list per kind.
  ///
  /// A payload of its own, not part of the Ledger export (decided 18 Oct 2026):
  /// the main export was cut to fmt 2 so it pastes cheaply, and these lists
  /// would undo that. Owned ids only — the readers hold the names and totals,
  /// the same way tm-export-tables already maps quest ids.
  ///
  /// Every read and its id source was checked with MemoriaProbe
  /// (CollectionsProbe, 18 Oct 2026). Mounts 34, minions 110 and facewear 8
  /// matched the in-game guides exactly.
  ///
  /// <list type="bullet">
  /// <item>Facewear is sent as style ids. The read takes a Glasses row, one per
  /// colour, but the game's Facewear window counts styles (Kaye: 96 colours,
  /// TOTAL 8), so the rows are grouped by their style.</item>
  /// <item>Fish and spearfish: the read takes the FishParameter or
  /// SpearfishingItem row and holds only so many bits (1522 and 304 today).
  /// Rows past that are not fish the game tracks; the walk stops at the first
  /// one, since rows come in ascending order.</item>
  /// <item>Bestiary only once the client holds the capture list; see the read.</item>
  /// </list>
  ///
  /// Not here yet: framer's kits (no known id-to-name mapping), Blue Magic,
  /// titles and achievements.
  /// </summary>
  private unsafe string CollectionsJson()
  {
    try
    {
      JsonObject root = new()
      {
        ["source"] = "time-memoria",
        ["kind"] = "collections",
        ["fmt"] = 2,
        ["version"] = typeof(LedgerExportService).Assembly.GetName().Version?.ToString() ?? "unknown",
        ["exported"] = Stamp(DateTime.UtcNow),
        ["name"] = _playerState.IsLoaded ? _playerState.CharacterName : string.Empty,
        ["world"] = WorldName(),
        ["game"] = GameBuild()
      };

      var player = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
      var ui = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();

      // Nothing to read without a character; empty lists here would tell the
      // ledger this character owns nothing.
      if (!_playerState.IsLoaded || player == null || ui == null)
        return root.ToJsonString();

      root["mounts"] = Owned(Named(_dataManager.GetExcelSheet<Mount>(), r => r.Singular), id => player->IsMountUnlocked(id));
      root["minions"] = Owned(Named(_dataManager.GetExcelSheet<Companion>(), r => r.Singular), id => ui->IsCompanionUnlocked(id));
      root["orchestrion"] = Owned(Named(_dataManager.GetExcelSheet<Orchestrion>(), r => r.Name), id => player->IsOrchestrionRollUnlocked(id));
      root["ttCards"] = Owned(Named(_dataManager.GetExcelSheet<TripleTriadCard>(), r => r.Name), id => ui->IsTripleTriadCardUnlocked((ushort)id));
      root["ttNpcs"] = Owned(_dataManager.GetExcelSheet<TripleTriadResident>().Select(r => r.RowId).Where(id => id > 0), id => ui->IsTripleTriadNpcBeaten(id));
      root["emotes"] = Owned(Named(_dataManager.GetExcelSheet<Emote>(), r => r.Name), id => ui->IsEmoteUnlocked((ushort)id));
      root["accessories"] = Owned(Named(_dataManager.GetExcelSheet<Ornament>(), r => r.Singular), id => player->IsOrnamentUnlocked(id));
      root["curios"] = Owned(_dataManager.GetExcelSheet<McGuffin>().Where(r => r.RowId > 0).Select(r => r.RowId), id => player->IsMcGuffinUnlocked(id));

      JsonArray styles = [];
      foreach (uint style in _dataManager.GetExcelSheet<Glasses>()
                 .Where(r => r.RowId > 0 && r.Style.RowId > 0 && player->IsGlassesUnlocked((ushort)r.RowId))
                 .Select(r => r.Style.RowId).Distinct().Order())
        styles.Add(style);
      root["facewear"] = styles;

      root["fish"] = OwnedUntilOutOfRange(_dataManager.GetExcelSheet<FishParameter>().Select(r => r.RowId), id => player->IsFishCaught(id));
      root["spearfish"] = OwnedUntilOutOfRange(_dataManager.GetExcelSheet<SpearfishingItem>().Select(r => r.RowId), id => player->IsSpearfishCaught(id));

      // Master's Bestiary: captured beasts by XBMPet row (1-50), the "No." the
      // game's window shows. Checked with MemoriaProbe's "bstpets" probe and in
      // game against that window (31 of 50 on Kaye, 22 Oct 2026). Left out until
      // the client has the list: before that every beast reads as not captured,
      // and an empty list would say none are. The sheet is read untyped: Lumina's
      // XBMPet row no longer matches the game's columns (MismatchedColumnHash,
      // 22 Oct 2026), and only the row ids are needed.
      var xbm = FFXIVClientStructs.FFXIV.Client.Game.XBMManager.Instance();
      if (xbm != null && xbm->State == FFXIVClientStructs.FFXIV.Client.Game.XBMManager.DataState.Received)
        root["bestiary"] = Owned(_dataManager.GetExcelSheet<Lumina.Excel.RawRow>(name: "XBMPet").Where(r => r.RowId > 0).Select(r => r.RowId),
          id => xbm->IsPetUnlocked(id));

      // Folklore tomes read, sent as the tome's Item id so a reader can name it
      // without another table. Not IsFolkloreBookUnlocked(Division): that read 19
      // of 19 on Kaye, unused divisions included. The game keeps one bit array per
      // job, and the bit is the GatheringSubCategory's Quest column (Coerthas is
      // bit 0). Confirmed 22 Oct 2026 by reading Yok Tural in game: Miner bit 9
      // flipped and nothing else moved (MemoriaProbe "folklore").
      JsonArray folklore = [];
      foreach ((uint job, byte[] bits) in new[]
      {
        (16u, System.Runtime.InteropServices.MemoryMarshal.AsBytes(player->UnlockedMinerFolkloreTomes).ToArray()),
        (17u, System.Runtime.InteropServices.MemoryMarshal.AsBytes(player->UnlockedBotanistFolkloreTomes).ToArray()),
        (18u, System.Runtime.InteropServices.MemoryMarshal.AsBytes(player->UnlockedFishingFolkloreTomes).ToArray())
      })
      {
        foreach (uint tome in _dataManager.GetExcelSheet<GatheringSubCategory>()
                   .Where(r => r.ClassJob.RowId == job && r.Quest.RowId < 74 && r.Item.RowId > 0)
                   .Where(r => r.Quest.RowId / 8 < bits.Length && (bits[r.Quest.RowId / 8] & (1 << (int)(r.Quest.RowId % 8))) != 0)
                   .Select(r => r.Item.RowId).Distinct().Order())
          folklore.Add(tome);
      }
      root["folklore"] = folklore;

      // World completion, checked with MemoriaProbe's WorldProbe (18 Oct 2026) and
      // PorterProbe (14 Oct 2026).
      //
      // Sightseeing takes an index, not a row: the Adventure sheet starts at row
      // 2162688 and the read wants the offset from there (the raw row id reads 0).
      // The export sends the index too, so it is the same number the read takes.
      var adventures = _dataManager.GetExcelSheet<Adventure>();
      uint adventureBase = adventures.Min(r => r.RowId);
      root["sightseeing"] = OwnedUntilOutOfRange(
        adventures.Where(r => !r.Name.IsEmpty).Select(r => r.RowId - adventureBase),
        id => player->IsAdventureComplete(id), allowZero: true);
      root["currents"] = Owned(_dataManager.GetExcelSheet<AetherCurrent>().Where(r => r.RowId > 0).Select(r => r.RowId),
        id => player->IsAetherCurrentUnlocked(id));
      root["currentZones"] = Owned(_dataManager.GetExcelSheet<AetherCurrentCompFlgSet>().Where(r => r.RowId > 0 && r.Territory.RowId > 0).Select(r => r.RowId),
        id => player->IsAetherCurrentZoneComplete(id));
      root["aetherytes"] = Owned(_dataManager.GetExcelSheet<Aetheryte>().Where(r => r.RowId > 0 && r.IsAetheryte && !r.Invisible && r.PlaceName.RowId > 0).Select(r => r.RowId),
        id => ui->IsAetheryteUnlocked(id));
      root["porters"] = Owned(_dataManager.GetExcelSheet<ChocoboTaxiStand>().Where(r => r.RowId > 0 && !r.PlaceName.IsEmpty).Select(r => r.RowId),
        id => ui->IsChocoboTaxiStandUnlocked(id));

      // Mount speed: TerritoryType id -> speed level, only for zones above 0. The
      // zone's MountSpeed row names what unlocks each level: Quest is level I,
      // Unknown0 level II where there is one. Each is a Quest row (65536 and up) or
      // an UnlockLink id (a riding map). Checked against every tab of the game's
      // Mount Speed window on Kaye with MemoriaProbe, 18 Oct 2026. Several
      // territories share a row (instanced copies), so each row is read once, under
      // its lowest territory id; the reader holds names and top levels.
      JsonObject mountSpeed = [];
      foreach (var zone in _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
                 .Where(t => t.Mount && t.MountSpeed.RowId != 0 && t.PlaceName.RowId != 0)
                 .GroupBy(t => t.MountSpeed.RowId)
                 .Select(g => g.OrderBy(t => t.RowId).First())
                 .OrderBy(t => t.RowId))
      {
        var row = zone.MountSpeed.Value;
        int level = (row.Quest.RowId != 0 && SpeedUnlocked(ui, row.Quest.RowId) ? 1 : 0)
                  + (row.Unknown0 != 0 && SpeedUnlocked(ui, row.Unknown0) ? 1 : 0);
        if (level > 0) mountSpeed[zone.RowId.ToString()] = level;
      }
      root["mountSpeed"] = mountSpeed;

      // Bicolor gemstones held (cap 1,500), the Shared FATE currency.
      InventoryManager* inventory = InventoryManager.Instance();
      if (inventory != null) root["gemstones"] = inventory->GetInventoryItemCount(26807);

      // Every completed quest, as short ids (RowId - 65536, what QuestManager
      // takes). The reader groups them by the game's own journal genres —
      // Hildibrand, Zodiac Weapons, Ishgardian Restoration, each seasonal event —
      // so a questline added in a patch needs no plugin change. Sorted, so two
      // exports diff cleanly.
      JsonArray questsDone = [];
      foreach (uint row in _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>()
                 .Where(q => q.RowId > 0 && !q.Name.IsEmpty).Select(q => q.RowId).Order())
        if (QuestManager.IsQuestComplete(row)) questsDone.Add(row & 0xFFFF);
      root["questsDone"] = questsDone;

      // Duty clears, by InstanceContent id (ContentFinderCondition.Content where
      // ContentLinkType is 1). Checked with MemoriaProbe's DutiesProbe, 18 Oct 2026:
      // every story duty before Dawntrail read completed on Kaye. Quest battles and
      // deep-dungeon floor sets never record a clear here; the reader covers those
      // from quests and achievements instead.
      JsonArray dutiesDone = [];
      foreach (uint content in _dataManager.GetExcelSheet<ContentFinderCondition>()
                 .Where(r => r.ContentLinkType == 1 && r.Content.RowId > 0 && !r.Name.IsEmpty)
                 .Select(r => r.Content.RowId).Distinct().Order())
        if (FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.IsInstanceContentCompleted(content)) dutiesDone.Add(content);
      root["dutiesDone"] = dutiesDone;

      root["relics"] = BuildRelics(inventory);

      return root.ToJsonString();
    }
    catch (Exception ex)
    {
      _pluginLog.Error(ex, "[LedgerExport] Failed to build collections payload");
      return "{}";
    }
  }

  /// <summary>
  /// Everything in one paste: the Ledger export exactly as BuildLedgerJson makes
  /// it, with the collections payload nested under "collections". Nested rather
  /// than merged, so a reader that only knows the Ledger export ignores one
  /// unknown key and keeps working. The collections header fields that repeat
  /// the outer ones (source, kind, fmt, version, exported, name, world) are
  /// dropped; "game" stays, since only the collections payload carries it.
  /// </summary>
  private string FullJson()
  {
    try
    {
      JsonObject? ledger = JsonNode.Parse(LedgerJson()) as JsonObject;
      JsonObject? collections = JsonNode.Parse(CollectionsJson()) as JsonObject;
      if (ledger is null || ledger.Count == 0) return "{}";

      if (collections is not null)
      {
        foreach (string key in new[] { "source", "kind", "fmt", "version", "exported", "name", "world" })
          collections.Remove(key);
        ledger["collections"] = collections;
      }

      return ledger.ToJsonString(new JsonSerializerOptions
      {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
      });
    }
    catch (Exception ex)
    {
      _pluginLog.Error(ex, "[LedgerExport] Failed to build full payload");
      return "{}";
    }
  }

  private static readonly InventoryType[] RelicContainers =
  [
    InventoryType.EquippedItems,
    InventoryType.ArmoryMainHand,
    InventoryType.ArmoryOffHand,
    InventoryType.Inventory1,
    InventoryType.Inventory2,
    InventoryType.Inventory3,
    InventoryType.Inventory4
  ];

  private static readonly string[] RelicLineOrder = ["ZD", "AN", "EU", "RS", "SK", "MV", "SP", "PH", "CO"];

  /// <summary>
  /// The highest relic stage held, per line and job, as one space-separated
  /// string: "ZDWAR-C RSWAR-A COCRP-J". Line code, job, then the stage as a
  /// letter (A is the first). One string rather than an array, so a pasted
  /// export costs fewer tokens; the stage names live in RelicStages and the
  /// reader's copy of the same table.
  ///
  /// Checked with MemoriaProbe's "relics" probe, 24 Oct 2026:
  /// <list type="bullet">
  /// <item>Equipped items, the armoury chest and the bags are read live. Relics do
  /// go in the armoury chest (Skullrender was there).</item>
  /// <item>The saddlebag, every retainer and the glamour dresser come from
  /// ItemFinderModule, the game's Search for Item cache. It is full from login
  /// (read from ITEMFDR.DAT, before any bell visit) and updates as soon as an
  /// item moves. The live saddlebag and retainer containers are not used: after
  /// login they say IsLoaded and hold nothing.</item>
  /// <item>Not covered: what a retainer has equipped, and the Armoire.</item>
  /// </list>
  /// </summary>
  private static unsafe string BuildRelics(InventoryManager* inventory)
  {
    Dictionary<(string Line, string Job), int> best = [];

    void Add(uint id)
    {
      // The cache lists can carry the high-quality offset; relics are never HQ,
      // but the base id is what the table holds.
      if (!RelicStages.ByItem.TryGetValue(id % 1_000_000, out var relic)) return;
      if (!best.TryGetValue((relic.Line, relic.Job), out int held) || relic.Stage > held)
        best[(relic.Line, relic.Job)] = relic.Stage;
    }

    if (inventory != null)
    {
      foreach (InventoryType type in RelicContainers)
      {
        InventoryContainer* container = inventory->GetInventoryContainer(type);
        if (container == null) continue;
        for (int slot = 0; slot < container->Size; slot++)
        {
          InventoryItem* item = container->GetInventorySlot(slot);
          if (item != null && item->ItemId != 0) Add(item->ItemId);
        }
      }
    }

    var finder = FFXIVClientStructs.FFXIV.Client.UI.Misc.ItemFinderModule.Instance();
    if (finder != null)
    {
      foreach (uint id in finder->SaddleBagItemIds) Add(id);
      foreach (uint id in finder->PremiumSaddleBagItemIds) Add(id);
      foreach (uint id in finder->GlamourDresserItemIds) Add(id);
      foreach (var (_, retainer) in finder->RetainerInventories)
        if (retainer.Value != null)
          foreach (uint id in retainer.Value->ItemIds) Add(id);
    }

    return string.Join(' ', best
      .OrderBy(b => Array.IndexOf(RelicLineOrder, b.Key.Line)).ThenBy(b => b.Key.Job, StringComparer.Ordinal)
      .Select(b => $"{b.Key.Line}{b.Key.Job}-{(char)('A' + b.Value)}"));
  }

  /// <summary>
  /// The client's build, "2026.10.06.0000.0000" — what ffxivgame.ver holds. The
  /// client knows its build, not its patch number, so this is what it can say.
  /// A reader compares it with the date its own item list was built: a newer
  /// build means content the list may not know yet (a PvP season's new mounts).
  /// </summary>
  private string GameBuild()
    => _dataManager.GameData.Repositories.TryGetValue("ffxiv", out Lumina.Data.Repository? repo)
      ? repo.Version ?? string.Empty
      : string.Empty;

  /// <summary>Rows with a name. A blank name is an unused slot, not a collectable.</summary>
  private static IEnumerable<uint> Named<T>(Lumina.Excel.ExcelSheet<T> sheet, Func<T, Lumina.Text.ReadOnly.ReadOnlySeString> name)
    where T : struct, Lumina.Excel.IExcelRow<T> =>
    sheet.Where(r => r.RowId > 0 && !name(r).IsEmpty).Select(r => r.RowId);

  /// <summary>A mount speed unlock: a Quest row id (65536 and up) or an UnlockLink id.</summary>
  private static unsafe bool SpeedUnlocked(FFXIVClientStructs.FFXIV.Client.Game.UI.UIState* ui, uint id) =>
    id >= 65536 ? QuestManager.IsQuestComplete(id) : ui->IsUnlockLinkUnlocked(id);

  private static JsonArray Owned(IEnumerable<uint> ids, Func<uint, bool> isOwned)
  {
    JsonArray owned = [];
    foreach (uint id in ids)
      if (isOwned(id)) owned.Add(id);
    return owned;
  }

  /// <summary>As <see cref="Owned"/>, stopping at the first id the bit array cannot hold.</summary>
  private static JsonArray OwnedUntilOutOfRange(IEnumerable<uint> ids, Func<uint, bool> isOwned, bool allowZero = false)
  {
    JsonArray owned = [];
    foreach (uint id in ids)
    {
      // Row 0 is an empty slot in most sheets; an index of 0 (sightseeing) is a real entry.
      if (id == 0 && !allowZero) continue;
      try
      {
        if (isOwned(id)) owned.Add(id);
      }
      catch (ArgumentOutOfRangeException)
      {
        break;
      }
    }
    return owned;
  }

  /// <summary>
  /// Which features this character has unlocked — see <see cref="LedgerUnlocks"/>
  /// for where the ids come from and why quest completion is the right question.
  ///
  /// Every key is emitted, true or false. An absent key has to mean "this build
  /// did not know about that unlock" so the ledger can fall back to its own patch
  /// gate; if false and absent were the same thing, adding a key later would read
  /// as everyone suddenly unlocking it.
  ///
  /// Null when the player is not loaded. Sending a payload of falses for a
  /// character the client cannot see would tell the ledger to hide everything.
  /// </summary>
  private JsonObject? BuildUnlocks()
  {
    if (!_playerState.IsLoaded) return null;

    JsonObject unlocks = [];

    // 1/0 rather than true/false: fmt 2, see BuildLedgerJson.
    foreach ((string key, uint[] ids) in LedgerUnlocks.Quests)
      unlocks[key] = ids.Any(QuestManager.IsQuestComplete) ? 1 : 0;

    // Squadrons, the daily Hunt bills and player housing all open at Second
    // Lieutenant, so one rank read gates several ledger rows. Rank is only
    // meaningful once enlisted — GetGrandCompanyRank wants a company to ask
    // about, and an unenlisted character has none.
    uint company = _playerState.GrandCompany.RowId;
    bool enlisted = company != 0;

    unlocks["grandCompany"] = enlisted ? 1 : 0;
    unlocks["squadron"] = enlisted &&
      _playerState.GetGrandCompanyRank(_playerState.GrandCompany.Value) >= LedgerUnlocks.SecondLieutenantRank ? 1 : 0;

    return unlocks;
  }

  /// <summary>
  /// Lifetime playtime with the age of the figure attached. Omitted entirely when
  /// the player has never run /playtime, since sending zeroes would overwrite a
  /// good value the ledger already holds.
  /// </summary>
  private JsonObject? BuildPlaytime()
  {
    PlaytimeRecord? record = _playtime.Current;
    if (record == null || record.LifetimePlaytime <= TimeSpan.Zero) return null;

    JsonObject playtime = new()
    {
      ["days"] = (int)record.LifetimePlaytime.TotalDays,
      ["hours"] = record.LifetimePlaytime.Hours
    };

    if (record.LifetimePlaytimeUpdatedUtc.HasValue)
      playtime["asOf"] = Stamp(record.LifetimePlaytimeUpdatedUtc.Value);

    return playtime;
  }

  /// <summary>
  /// Allied society standing, which the ledger has until now asked people to
  /// enter by hand — and which was consequently a rank and three hundred points
  /// out of date in the file that prompted this.
  ///
  /// Rank and points are durable facts about a character and belong here. The
  /// daily allowance deliberately does not: it resets every day, so a stored
  /// snapshot of it is worse than nothing.
  ///
  /// Every society is sent, including untouched ones at rank 0, because "not
  /// started" is a fact the ledger cannot otherwise distinguish from "never
  /// synced".
  ///
  /// Keyed by sheet id rather than by name. There are at least four spellings in
  /// circulation for the same society — the sheet says "sylphs", the wiki says
  /// "Sylphs", the ledger says "Sylph" — and an import that matches on wording
  /// breaks the day any of them is revised. The id is the same number the client
  /// indexes standing by and cannot drift. The readers hold the names.
  ///
  /// fmt 2: <c>"1": [rank, points, needed]</c>, one entry per society.
  /// </summary>
  private JsonObject BuildSocieties()
  {
    JsonObject societies = [];

    foreach (SocietyStanding standing in _societies.GetStandings())
      societies[standing.Index.ToString(CultureInfo.InvariantCulture)] =
        new JsonArray(standing.Rank, standing.Points, standing.Needed);

    return societies;
  }

  /// <summary>
  /// Collectables gathered and synthesised — the two counts the ledger's Trade
  /// Mentor section has until now asked people to type in.
  ///
  /// Unlike everything else here these are not free to read. The game keeps no
  /// running counter; the number lives in an achievement and the client only
  /// holds whichever one the player last looked at. So each count carries the
  /// moment it was seen, and the ledger is expected to show that age rather than
  /// present a week-old figure as current.
  ///
  /// <c>exact</c> is false when the tier that was read is already complete: a
  /// finished tier reports its own requirement, not the running total, so the
  /// number is a floor. The distinction matters for a threshold — "300 of 300"
  /// derived from a floor is not the same claim as reaching 300.
  ///
  /// A side never read is omitted entirely rather than sent as zero, for the
  /// same reason playtime is: a zero here would overwrite a good value the
  /// ledger already holds. The whole block goes when neither has been read.
  /// </summary>
  private JsonObject? BuildCollectables()
  {
    JsonObject collectables = [];

    if (Reading(_achievements.Gathered) is { } gathered) collectables["gathered"] = gathered;
    if (Reading(_achievements.Crafted) is { } crafted) collectables["crafted"] = crafted;

    return collectables.Count > 0 ? collectables : null;
  }

  /// <summary>
  /// One achievement-derived figure with everything needed to judge it: the
  /// number, whether it is exact or a floor, and when it was taken. Null when the
  /// series has never been read, so the field is omitted rather than sent as a
  /// zero that would overwrite something better.
  /// </summary>
  private static JsonObject? Reading(AchievementReading? reading) => reading is null ? null : new JsonObject
  {
    ["count"] = reading.Value,
    ["exact"] = reading.IsExact,
    ["asOf"] = Stamp(reading.TakenUtc)
  };

  /// <summary>
  /// UTC to the second. The fraction the "o" format adds is seven digits that
  /// no reader uses, repeated on every timestamp.
  /// </summary>
  private static string Stamp(DateTime utc) =>
    utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

  /// <summary>
  /// Every completed class, job and role quest, by quest id (see CollectComplete).
  ///
  /// The ledger has these as a hand-ticked checkbox per quest, which is a hundred
  /// and fifty clicks describing something the game already knows exactly. A flat
  /// list is enough: the ledger holds its own quest-to-job mapping, so it only
  /// needs to be told which are done.
  ///
  /// Flat also means role quests arrive without being asked for — they share this
  /// category with job quests — so any the ledger chooses to list will start
  /// ticking themselves.
  ///
  /// Completed only. The full list would be several times longer and every entry
  /// absent from this one is, by definition, not done.
  /// </summary>
  private JsonArray BuildClassQuests()
  {
    JsonArray done = [];

    foreach (QuestData expansion in _dataService.QuestData.Categories)
      foreach (QuestData category in expansion.Categories)
        if (category.EnglishTitle == "Class & Job Quests")
          CollectComplete(category, done);

    return done;
  }

  /// <summary>
  /// Hide is deliberately not consulted. It carries the Display setting — "show
  /// only completed" or "show only incomplete" — so under the latter every
  /// completed quest is hidden and reading it here would export an empty list.
  /// What a character has finished is not a display preference.
  /// </summary>
  /// <remarks>
  /// fmt 2 sends the quest id, not the title: a number is a fraction of the
  /// size, cannot drift with wording, and is the same in every client
  /// language (the title is not). The short form, RowId - 65536, is the id
  /// QuestManager itself takes. Where one quest has several ids (the ARR
  /// start-city variants), the one completed is sent. The readers turn ids
  /// back into English titles from their own table.
  /// </remarks>
  private void CollectComplete(QuestData node, JsonArray into)
  {
    foreach (Types.Quest quest in node.Quests)
      if (_dataService.IsQuestComplete(quest))
        into.Add((quest.Ids.FirstOrDefault(QuestManager.IsQuestComplete, quest.Ids[0])) & 0xFFFF);

    foreach (QuestData child in node.Categories) CollectComplete(child, into);
  }

  /// <summary>
  /// Completed quests per top-level journal section, in the ledger's own keys.
  ///
  /// These counts were previously typed in by hand off the plugin's Overview,
  /// which meant they were only ever as fresh as the last time someone
  /// remembered to do it.
  ///
  /// Counts are read from the category nodes directly, so the Settings toggles
  /// for excluding Other Quests and Levequests do not reach this. Those change
  /// what the plugin chooses to display; they are not a statement about what the
  /// character has done, and a display preference must not silently rewrite
  /// someone else's stored data.
  /// </summary>
  private JsonObject BuildQuests()
  {
    // The ledger has no bucket for Other Quests, so its Overall is the sum of
    // the six it does track -- and its UI asserts exactly that. Sending our own
    // Overall here would include Other Quests and break that check.
    Dictionary<string, string> keys = new()
    {
      ["Main Scenario"] = "msq",
      ["Chronicles of a New Era"] = "era",
      ["Sidequests"] = "side",
      ["Allied Society Quests"] = "allied",
      ["Class & Job Quests"] = "class",
      ["Levequests"] = "leve"
    };

    Dictionary<string, int> totals = keys.Values.ToDictionary((k) => k, (_) => 0);

    // Sections live under each expansion, so a section's total is the sum of its
    // appearances across all of them.
    foreach (QuestData expansion in _dataService.QuestData.Categories)
      foreach (QuestData category in expansion.Categories)
        if (keys.TryGetValue(category.EnglishTitle, out string? key))
          totals[key] += (int)category.NumComplete;

    JsonObject quests = new() { ["overall"] = totals.Values.Sum() };
    foreach (KeyValuePair<string, string> pair in keys)
      quests[pair.Value] = totals[pair.Value];

    // [done, total] per expansion, oldest first, over the same six sections. The
    // ledger's route line fills each station from this, so its figures are the
    // plugin's own rather than a second count that leaves out levequests.
    JsonArray byExpansion = [];
    foreach (QuestData expansion in _dataService.QuestData.Categories)
    {
      int done = 0, total = 0;
      foreach (QuestData category in expansion.Categories)
        if (keys.ContainsKey(category.EnglishTitle))
        {
          done += (int)category.NumComplete;
          total += (int)category.Total;
        }
      byExpansion.Add(new JsonArray(done, total));
    }
    quests["byExp"] = byExpansion;

    return quests;
  }

  /// <summary>
  /// Story position as a patch number, which is how people describe it to each
  /// other — "I'm on 6.3", not "I have done 812 Main Scenario quests".
  ///
  /// Two values, because they answer different questions. <c>cleared</c> is the
  /// last patch finished and is the safe one to gate on. <c>reached</c> is the
  /// patch being played now, and can be one ahead.
  ///
  /// Omitted entirely when neither is known, rather than sent as nulls that a
  /// consumer might store over a good value.
  /// </summary>
  private JsonObject? BuildMsqPatch()
  {
    MsqPatchProgress progress = _tocService.GetMsqPatchProgress();
    if (progress.Cleared is null && progress.Reached is null) return null;

    JsonObject patch = [];
    if (progress.Cleared is not null) patch["cleared"] = progress.Cleared;
    if (progress.Reached is not null) patch["reached"] = progress.Reached;

    return patch;
  }

  /// <summary>
  /// Completed MSQ per expansion. The tree is nested under expansion, so each one
  /// carries its own Main Scenario section and no patch mapping is needed.
  /// </summary>
  private JsonObject BuildMsqBreakdown()
  {
    Dictionary<uint, string> keys = new()
    {
      [0] = "arr", [1] = "hw", [2] = "stb", [3] = "shb", [4] = "ew", [5] = "dt"
    };

    JsonObject breakdown = [];

    foreach (QuestData expansion in _dataService.QuestData.Categories)
    {
      if (!keys.TryGetValue(expansion.SortKey, out string? key)) continue;

      QuestData? msq = expansion.Categories.FirstOrDefault((c) => c.EnglishTitle == "Main Scenario");
      breakdown[key] = msq != null ? (int)msq.NumComplete : 0;
    }

    return breakdown;
  }

  /// <summary>
  /// Cosmic Tool research data, per class, in the shape
  /// <see cref="ICosmicToolProgressService"/> already filters to "worth
  /// reporting" — see that service for why untouched classes and irrelevant
  /// types are absent rather than zero.
  ///
  /// Carries its own <c>asOf</c> because, unlike most of this export, the value
  /// is not necessarily current: the underlying module can only be read while
  /// physically inside a Cosmic Exploration zone, so this may be reporting a
  /// reading from an earlier session. See
  /// <see cref="ICosmicToolProgressService"/> for why that is a persisted last
  /// reading rather than a live-or-nothing value.
  ///
  /// Whole section omitted when nothing has ever been read, same reasoning as
  /// playtime and collectables: a character with no reading yet must not send
  /// an empty object that would overwrite a good value already on the ledger.
  ///
  /// fmt 2: jobs keyed by abbreviation, each type as
  /// <c>[type, current, needed, max]</c>.
  /// </summary>
  private JsonObject? BuildCosmicTools(List<ClassJobProgress> levels)
  {
    List<CosmicToolProgress> progress = _cosmicTools.GetProgress();
    if (progress.Count == 0 || _cosmicTools.TakenUtc is not { } takenUtc) return null;

    Dictionary<string, string> abbreviations = levels
      .GroupBy(l => l.Name)
      .ToDictionary(g => g.Key, g => g.First().EnglishAbbreviation);

    JsonObject jobs = [];

    foreach (CosmicToolProgress job in progress)
    {
      JsonArray types = [];

      foreach (CosmicResearchProgress type in job.Types)
        types.Add(new JsonArray(type.Type, type.Current, type.Needed, type.Max));

      jobs[abbreviations.GetValueOrDefault(job.Job, job.Job)] = types;
    }

    return new JsonObject { ["asOf"] = Stamp(takenUtc), ["jobs"] = jobs };
  }

  /// <summary>
  /// Master recipe books (the SecretRecipeBook sheet), learned 1 or not 0.
  /// Learned only: which books to buy, and with what, is the ledger's job.
  ///
  /// One string per crafter: a digit per numbered book from 1 up, then "-"
  /// and a digit per other book, Glamours first, then Demimateria.
  /// <c>"GSM":"111000000000-11"</c> is GSM 1-3 learned, 4-12 not, both others
  /// learned. Culinarian has no other books, so its string has no "-".
  ///
  /// Null when the player is not loaded, so a payload of zeroes cannot
  /// overwrite what the ledger already holds.
  /// </summary>
  private unsafe JsonObject? BuildMasterBooks()
  {
    if (!_playerState.IsLoaded) return null;

    var player = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
    if (player == null) return null;

    JsonObject books = [];

    foreach ((string job, List<uint> numbered, List<uint> others) in MasterBooks())
    {
      string digits = string.Concat(numbered.Select(id => player->IsSecretRecipeBookUnlocked(id) ? '1' : '0'));
      if (others.Count > 0)
        digits += "-" + string.Concat(others.Select(id => player->IsSecretRecipeBookUnlocked(id) ? '1' : '0'));

      books[job] = digits;
    }

    return books;
  }

  /// <summary>
  /// Per crafter abbreviation, in crafter order: the numbered books by number,
  /// then the others (Glamours, Demimateria, then any new kind by row).
  /// Read from the English sheets so the split by name is the same in every
  /// client language.
  ///
  /// The crafter comes from the recipes that name the book, and a book no
  /// recipe names is left out: it has nothing to unlock (Culinarian Glamours,
  /// row 16). MemoriaProbe (masterbooks, 17 Oct 2026) found every book this
  /// way; the second gate, NotebookDivision.SecretRecipeBookGroups, added
  /// none, so it is not read. CraftType 0-7 is ClassJob 8-15.
  /// </summary>
  private List<(string Job, List<uint> Numbered, List<uint> Others)> MasterBooks()
  {
    if (_masterBooks != null) return _masterBooks;

    Dictionary<uint, uint> craftByBook = [];
    foreach (Recipe recipe in _dataManager.GetExcelSheet<Recipe>())
      if (recipe.SecretRecipeBook.RowId > 0)
        craftByBook.TryAdd(recipe.SecretRecipeBook.RowId, recipe.CraftType.RowId);

    // craft -> (sort key, book row). Numbered books sort by number; others
    // sort after them: Glamours, Demimateria, then by row.
    Dictionary<uint, List<(int Sort, bool Numbered, uint Id)>> byCraft = [];

    foreach (SecretRecipeBook book in _dataManager.GetExcelSheet<SecretRecipeBook>(ClientLanguage.English))
    {
      if (!craftByBook.TryGetValue(book.RowId, out uint craft)) continue;

      string name = book.Name.ToString();
      int number = RomanToNumber(name[(name.LastIndexOf(' ') + 1)..]);
      (int sort, bool numbered) = number > 0 ? (number, true)
        : name.EndsWith(": Glamours") ? (1_000_000, false)
        : name.EndsWith(": Demimateria") ? (1_000_001, false)
        : (2_000_000 + (int)book.RowId, false);

      if (!byCraft.TryGetValue(craft, out var list)) byCraft[craft] = list = [];
      list.Add((sort, numbered, book.RowId));
    }

    Lumina.Excel.ExcelSheet<ClassJob> jobs = _dataManager.GetExcelSheet<ClassJob>(ClientLanguage.English);
    List<(string, List<uint>, List<uint>)> result = [];

    foreach ((uint craft, var books) in byCraft.OrderBy(p => p.Key))
    {
      if (jobs.GetRowOrDefault(8 + craft) is not { } job) continue;

      var ordered = books.OrderBy(b => b.Sort).ToList();
      result.Add((job.Abbreviation.ToString(),
                  ordered.Where(b => b.Numbered).Select(b => b.Id).ToList(),
                  ordered.Where(b => !b.Numbered).Select(b => b.Id).ToList()));
    }

    return _masterBooks = result;
  }

  /// <summary>"XII" -> 12. Zero for anything that is not a roman numeral.</summary>
  private static int RomanToNumber(string text)
  {
    Dictionary<char, int> values = new() { ['I'] = 1, ['V'] = 5, ['X'] = 10, ['L'] = 50 };
    int total = 0;

    for (int i = 0; i < text.Length; i++)
    {
      if (!values.TryGetValue(text[i], out int value)) return 0;
      bool smallerBeforeLarger = i + 1 < text.Length && values.TryGetValue(text[i + 1], out int next) && next > value;
      total += smallerBeforeLarger ? -value : value;
    }

    return total;
  }

  /// <summary>
  /// Levels the way the ledger stores them: level plus the fraction of the way
  /// through it, to three decimals. Max level emits a bare integer. fmt 2 keys
  /// by abbreviation, "PLD" rather than "Paladin".
  /// </summary>
  private static JsonObject BuildLevels(List<ClassJobProgress> progress, string category)
  {
    JsonObject obj = [];

    foreach (ClassJobProgress job in progress.Where(p => p.Category == category))
    {
      if (job.Level == 0 || job.IsMaxLevel) obj[job.EnglishAbbreviation] = job.Level;
      else obj[job.EnglishAbbreviation] = Math.Round(job.Level + job.Fraction, 3, MidpointRounding.AwayFromZero);
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
