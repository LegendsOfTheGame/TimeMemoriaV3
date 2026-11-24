using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using BaseParamRow = Lumina.Excel.Sheets.BaseParam;
using ClassJobRow = Lumina.Excel.Sheets.ClassJob;
using EquipSlotRow = Lumina.Excel.Sheets.EquipSlotCategory;
using ItemRow = Lumina.Excel.Sheets.Item;

namespace TimeMemoria.Services;

/// <summary>One reward option against the worn piece it would replace.</summary>
/// <param name="Replaces">The worn piece, or "empty slot". Empty when there is nothing to compare.</param>
/// <param name="Changes">"Strength +2", one per change, counting only what the class uses.</param>
/// <param name="Improved">How many counted bonuses go up.</param>
/// <param name="Counted">How many counted bonuses either piece has.</param>
/// <param name="Total">Every change added up.</param>
/// <param name="Marked">Most improved. Several options can be marked when they tie.</param>
/// <param name="Note">Why it was not compared, or null when it was.</param>
public record GearOption(string Name, string Replaces, IReadOnlyList<string> Changes, int Improved, int Counted, int Total,
  bool Marked, string? Note);

public interface IGearRewardService
{
  /// <summary>
  /// The options on the open quest turn-in window, or empty when it is closed or
  /// none of its options is gear. Main thread only.
  /// </summary>
  IReadOnlyList<GearOption> Read();

  /// <summary>Whether an item id is something worn. Accepts the HQ form (id plus one million).</summary>
  bool IsGear(uint itemId);
}

/// <summary>
/// The gear a quest turn-in offers, against what is worn, for the current class.
///
/// Prime Directive amendment of 24 Nov 2026. Haven's rule: the largest total gain
/// over the worn piece is marked "most improved", counting only the bonuses the
/// class uses. A tie goes to the larger share improved; a further tie marks all.
/// It reads only, and it never selects or equips anything.
///
/// MemoriaProbe's "gearreward" probe proved the reads the same day. Not yet seen
/// in game: HQ rewards, melded gear, rings, weapons.
///
/// A piece with the generic Main or Secondary Attribute is never compared. The
/// game's own read returns those unresolved and unscaled, and such a piece can
/// carry an EXP bonus that no total sees (Neophyte's Ring, +30%).
/// </summary>
public unsafe class GearRewardService(ILogger _logger, IDataManager _dataManager, IClientState _clientState)
  : IGearRewardService
{
  // JournalResult's AtkValues; see TurnInFoodEye for the rest of the layout.
  private const int OptionCount = 81;
  private const int OptionIds = 82;
  private const int OptionQuantities = 93;
  private const int OptionSlots = 5;

  private const uint HqOffset = 1_000_000;
  private const uint VitalityParam = 3;
  private const uint MainAttributeParam = 55;
  private const uint SecondaryAttributeParam = 56;

  public bool IsGear(uint itemId) =>
    _dataManager.GetExcelSheet<ItemRow>().GetRowOrDefault(itemId % HqOffset) is { EquipSlotCategory.RowId: not 0 };

  public IReadOnlyList<GearOption> Read()
  {
    if (!_clientState.IsLoggedIn) return [];

    try
    {
      return ReadOptions();
    }
    catch (Exception ex)
    {
      // The food list below this section must survive a bad read here.
      _logger.Error(ex, "[Gear] Could not read the turn-in rewards.");
      return [];
    }
  }

  private IReadOnlyList<GearOption> ReadOptions()
  {
    RaptureAtkUnitManager* units = RaptureAtkUnitManager.Instance();
    AtkUnitBase* addon = units is null ? null : units->GetAddonByName("JournalResult");
    if (addon is null || !addon->IsVisible) return [];

    Span<AtkValue> values = new(addon->AtkValues, addon->AtkValuesCount);
    if (values.Length < OptionQuantities + OptionSlots) return [];

    PlayerState* state = PlayerState.Instance();
    InventoryManager* manager = InventoryManager.Instance();
    InventoryContainer* worn = manager is null ? null : manager->GetInventoryContainer(InventoryType.EquippedItems);
    if (state is null || worn is null) return [];

    // English, because ClassJobCategory's columns are named by the English
    // abbreviation whatever language the game runs in.
    ClassJobRow? job = _dataManager.GetExcelSheet<ClassJobRow>(ClientLanguage.English)
      .GetRowOrDefault(state->CurrentClassJobId);
    if (job is null) return [];

    HashSet<uint> used = UsedParams(job.Value);
    List<GearOption> compared = [];
    List<GearOption> others = [];
    bool anyGear = false;

    uint count = Math.Min(UInt(values[OptionCount]), OptionSlots);

    for (int i = 0; i < count; i++)
    {
      uint raw = UInt(values[OptionIds + i]);
      ItemRow? item = _dataManager.GetExcelSheet<ItemRow>().GetRowOrDefault(raw % HqOffset);
      if (raw == 0 || item is null) continue;

      anyGear |= item.Value.EquipSlotCategory.RowId != 0;

      GearOption option = Compare(item.Value, raw >= HqOffset, Math.Max(1, UInt(values[OptionQuantities + i])),
        job.Value.Abbreviation.ToString(), worn, used, state->CurrentLevel);

      (option.Note is null ? compared : others).Add(option);
    }

    if (!anyGear) return [];
    if (compared.Count == 0) return others;

    int top = compared.Max((o) => o.Total);
    double share = compared.Where((o) => o.Total == top).Max(Share);

    // A total of zero or less improves nothing, so nothing is marked.
    IEnumerable<GearOption> ordered = compared
      .Select((o) => o with { Marked = o.Total > 0 && o.Total == top && Share(o) == share })
      .OrderByDescending((o) => o.Total)
      .ThenByDescending(Share);

    return [.. ordered, .. others];
  }

  private static double Share(GearOption option) =>
    option.Counted == 0 ? 0 : (double)option.Improved / option.Counted;

  private static GearOption Skip(string name, string note) => new(name, "", [], 0, 0, 0, false, note);

  /// <summary>
  /// The change against every worn piece the reward would displace. A ring
  /// displaces one of two, so both are tried and the larger gain is kept.
  /// </summary>
  private GearOption Compare(ItemRow item, bool highQuality, uint quantity, string abbreviation,
    InventoryContainer* worn, HashSet<uint> used, int level)
  {
    string name = item.Name.ToString() + (highQuality ? " (HQ)" : "");
    if (quantity > 1) name = $"{quantity}x {name}";

    if (item.EquipSlotCategory.RowId == 0)
      return Skip(name, item.PriceLow > 0 ? $"Sells for {item.PriceLow * quantity} gil." : "Not gear.");
    if (!Wearable(item, abbreviation)) return Skip(name, $"Not for {abbreviation}.");
    if (Generic(item)) return Skip(name, "Not compared: its bonuses change with class and level.");

    Dictionary<uint, int> reward = SheetStats(item, highQuality);
    if (!used.Any((p) => reward.GetValueOrDefault(p) != 0)) return Skip(name, "No bonuses this class uses.");

    if (item.LevelEquip > level) name += $" (Lv. {item.LevelEquip})";

    GearOption? best = null;
    List<string> blocked = [];

    foreach (int[] slots in Displaced(item))
    {
      Dictionary<uint, int> current = [];
      List<string> names = [];
      bool comparable = true;

      foreach (int slot in slots)
      {
        InventoryItem* piece = worn->GetInventorySlot(slot);
        if (piece is null || piece->ItemId == 0) continue;

        ItemRow? row = _dataManager.GetExcelSheet<ItemRow>().GetRowOrDefault(piece->ItemId);
        string pieceName = row?.Name.ToString() ?? "an unknown item";
        names.Add(pieceName);

        if (row is null || Generic(row.Value))
        {
          comparable = false;
          blocked.Add(pieceName);
          continue;
        }

        // The game's own total for a real worn item, HQ and materia included.
        // Never call it on an InventoryItem built here: the struct has a vtable.
        foreach (uint param in used)
          current[param] = current.GetValueOrDefault(param) +
                           (int)InventoryItem.GetParameterValue(param, piece, true, true, false, false);
      }

      if (!comparable) continue;

      List<(uint Param, int Change)> changes = used
        .Select((p) => (Param: p, Change: reward.GetValueOrDefault(p) - current.GetValueOrDefault(p)))
        .Where((c) => c.Change != 0)
        .ToList();

      List<string> text = changes.Select((c) => $"{ParamName(c.Param)} {c.Change:+0;-0}").ToList();
      if (text.Count == 0) text.Add("No change");
      if (item.DamagePhys != 0 || item.DamageMag != 0) text.Add("weapon damage not counted");

      GearOption candidate = new(name,
        names.Count == 0 ? "empty slot" : string.Join(" + ", names),
        text,
        changes.Count((c) => c.Change > 0),
        // Either side having it counts, so an unchanged bonus is still "of N".
        used.Count((p) => reward.GetValueOrDefault(p) != 0 || current.GetValueOrDefault(p) != 0),
        changes.Sum((c) => c.Change),
        false, null);

      if (best is null || candidate.Total > best.Total) best = candidate;
    }

    return best ?? Skip(name, $"Not compared with {string.Join(", ", blocked)}: its bonuses change with class and level.");
  }

  /// <summary>
  /// EquippedItems indices for each way of wearing the item: what it fills plus
  /// what it blocks. The columns run in container order (waist is index 5).
  /// </summary>
  private static List<int[]> Displaced(ItemRow item)
  {
    EquipSlotRow row = item.EquipSlotCategory.Value;
    if (row.FingerL > 0 && row.FingerR > 0) return [[11], [12]];

    int[] columns =
    [
      row.MainHand, row.OffHand, row.Head, row.Body, row.Gloves, row.Waist, row.Legs, row.Feet,
      row.Ears, row.Neck, row.Wrists
    ];

    return [Enumerable.Range(0, columns.Length).Where((i) => columns[i] != 0).ToArray()];
  }

  private static bool Generic(ItemRow item)
  {
    for (int i = 0; i < item.BaseParam.Count; i++)
      if (item.BaseParam[i].RowId is MainAttributeParam or SecondaryAttributeParam && item.BaseParamValue[i] != 0)
        return true;

    return false;
  }

  /// <summary>The special values are HQ values only when the bonus is type 1; other types are set bonuses.</summary>
  private static Dictionary<uint, int> SheetStats(ItemRow item, bool highQuality)
  {
    Dictionary<uint, int> stats = [];

    for (int i = 0; i < item.BaseParam.Count; i++) Add(item.BaseParam[i].RowId, item.BaseParamValue[i]);

    if (highQuality && item.ItemSpecialBonus.RowId == 1)
      for (int i = 0; i < item.BaseParamSpecial.Count; i++)
        Add(item.BaseParamSpecial[i].RowId, item.BaseParamValueSpecial[i]);

    return stats;

    void Add(uint param, int value)
    {
      if (param != 0 && value != 0) stats[param] = stats.GetValueOrDefault(param) + value;
    }
  }

  /// <summary>
  /// BaseParam ids the class uses. Main stat from the sheet (so no Dexterity on
  /// Lancer), the rest by role.
  /// </summary>
  private static HashSet<uint> UsedParams(ClassJobRow job)
  {
    if (job.RowId is >= 8 and <= 15) return [70, 71, 11];   // Craftsmanship, Control, CP
    if (job.RowId is >= 16 and <= 18) return [72, 73, 10];  // Gathering, Perception, GP

    // Critical Hit, Determination, Direct Hit Rate, then Spell or Skill Speed.
    HashSet<uint> used = [job.PrimaryStat, VitalityParam, 27, 44, 22];
    used.Add(job.PrimaryStat is 4 or 5 ? 46u : 45u);
    if (job.Role == 1) used.Add(19);  // Tenacity
    if (job.Role == 4) used.Add(6);   // Piety

    return used;
  }

  private static bool Wearable(ItemRow item, string abbreviation)
  {
    object category = item.ClassJobCategory.Value;
    return category.GetType().GetProperty(abbreviation)?.GetValue(category) is true;
  }

  private string ParamName(uint id) =>
    _dataManager.GetExcelSheet<BaseParamRow>().GetRowOrDefault(id)?.Name.ToString() ?? "";

  private static uint UInt(AtkValue value) => value.Type == AtkValueType.UInt ? value.UInt : 0;
}
