using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using PlayerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;
using PlayerAttribute = Dalamud.Game.Player.PlayerAttribute;
using ItemFoodRow = Lumina.Excel.Sheets.ItemFood;
using ItemRow = Lumina.Excel.Sheets.Item;
using QuestRow = Lumina.Excel.Sheets.Quest;

namespace TimeMemoria.Services;

/// <summary>What the player is holding, and whether they are already fed.</summary>
/// <param name="WellFed">Whether the well-fed status is currently active.</param>
/// <param name="RemainingSeconds">How long that has left. Zero when not fed.</param>
/// <param name="Active">
/// The meal currently granting well-fed, with how many of that same food are
/// still in the bags. Null when not fed.
/// </param>
/// <param name="Banked">
/// Total well-fed time sitting unused in the bags — every item's own duration
/// times how many are held. The point of the panel in one number.
/// </param>
/// <param name="Best">The suggestion, or null when there is no food at all.</param>
/// <param name="Held">
/// Every distinct food item currently carried, one entry per item id with its
/// stat bonuses — the answer to "what food do I have and what does it do",
/// which Active/Best alone do not cover.
/// </param>
public record FoodReading(bool WellFed, float RemainingSeconds, FoodChoice? Active, TimeSpan Banked, FoodChoice? Best,
  IReadOnlyList<FoodStack> Held);

/// <param name="Role">DPS, Healer, Tank, Crafter or Gatherer, for colouring the name.</param>
public record FoodChoice(uint ItemId, string Name, bool HighQuality, int Quantity, string Role = "");

/// <summary>
/// One food item's stat bonuses, which pursuit it serves, the role its
/// bonuses best match (for colour coding), and a ranking within its pursuit —
/// value to the currently equipped job, the same cap-first reasoning
/// FoodService uses to pick a single Best. Job-relevant rather than a raw
/// stat total: Combat isn't one audience the way Crafting and Gathering are,
/// so a food full of Skill Speed still needs to rank behind a Piety food for
/// someone playing a healer.
/// </summary>
/// <param name="Where">
/// Every place this food is held, in both qualities, one "3x Name in Place" per
/// line -- the bags, the saddlebag, the premium saddlebag, then each retainer by
/// name. Retainer copies are listed here only; <paramref name="Quantity"/>
/// counts what is carried, in this row's quality.
/// </param>
public record FoodStack(string Name, string Stats, FoodPursuit Pursuit, string Role, int Score, bool HighQuality,
  bool IsActive, int Quantity, string Where = "");

/// <summary>What kind of play a food's stats are for — the axis food actually splits on in this game.</summary>
public enum FoodPursuit { Combat, Crafting, Gathering }

/// <summary>
/// The strongest food held for one role, and where it is. Name is null when
/// nothing held suits that role.
/// </summary>
/// <param name="Role">DPS, Healer, Tank, Crafter or Gatherer.</param>
/// <param name="Where">"Inventory", "Saddlebag", or the retainer's name.</param>
public record RolePick(string Role, string? Name, bool HighQuality, string? Where);

/// <summary>An accepted quest that rewards food, and what it gives.</summary>
/// <param name="Fixed">Food given on completion whatever is chosen.</param>
/// <param name="Choice">Food among the rewards the player picks one of.</param>
public record QuestFood(uint QuestId, string QuestName, IReadOnlyList<QuestFoodItem> Fixed,
  IReadOnlyList<QuestFoodItem> Choice);

/// <param name="Role">DPS, Healer, Tank, Crafter or Gatherer; empty for a meal that suits no role.</param>
public record QuestFoodItem(string Name, int Quantity, bool HighQuality, string Role);

public interface IFoodService
{
  /// <summary>
  /// Reads bags and status. Cheap enough to call when a window opens; not
  /// cached, because both inputs change while the window is open.
  /// </summary>
  FoodReading Read();

  /// <summary>
  /// One pick per role, from the bags, the saddlebag and every retainer.
  /// Unlike <see cref="Read"/>, which only suggests food already carried.
  /// </summary>
  IReadOnlyList<RolePick> BestByRole();

  /// <summary>
  /// The food each accepted quest gives on completion, from the Quest sheet.
  /// Quests that give no food are left out.
  /// </summary>
  IReadOnlyList<QuestFood> FromAcceptedQuests();

  /// <summary>Whether an item id is a meal. Accepts the HQ form (id plus one million).</summary>
  bool IsMeal(uint itemId);
}

/// <summary>
/// Which of the food you are already carrying is worth eating.
///
/// The premise is not that players buy food — it is that the game hands it out
/// constantly, the main scenario alone rewards it every few quests, and almost
/// nobody eats any of it. So the job is less "find the optimal meal" than "tell
/// them what is in their own bags".
///
/// Two things make this smaller than it first appears. Every meal carries the
/// same experience bonus whatever its stats, so *something* is always the right
/// answer and the panel is never empty. And food grants a percentage with a hard
/// cap, which above a few hundred in a stat is always the binding constraint —
/// so the ranking is a sort by cap rather than live arithmetic.
///
/// See Docs/gear-and-food.md.
/// </summary>
public unsafe class FoodService(ILogger _logger, IDataManager _dataManager, IClientState _clientState,
  IPlayerState _playerState) : IFoodService
{
  /// <summary>
  /// BaseParam names to the Dalamud attribute that reads their live total.
  /// Confirmed against MemoriaProbe's attributes probe: GetAttribute matches the
  /// Character window exactly, and a fed/unfed A/B on three different foods
  /// (Pastry Fish, Eft Steak) reproduced min(unfed_base * food%, cap) to the
  /// point. Only the twelve stats a food can actually grant are listed --
  /// StatText/Classify never produce a name outside this set.
  /// </summary>
  private static readonly Dictionary<string, PlayerAttribute> AttributeByParamName =
    new(StringComparer.OrdinalIgnoreCase)
    {
      ["Vitality"] = PlayerAttribute.Vitality,
      ["Critical Hit"] = PlayerAttribute.CriticalHit,
      ["Determination"] = PlayerAttribute.Determination,
      ["Direct Hit Rate"] = PlayerAttribute.DirectHitRate,
      ["Skill Speed"] = PlayerAttribute.SkillSpeed,
      ["Spell Speed"] = PlayerAttribute.SpellSpeed,
      ["Tenacity"] = PlayerAttribute.Tenacity,
      ["Piety"] = PlayerAttribute.Piety,
      ["Craftsmanship"] = PlayerAttribute.Craftsmanship,
      ["Control"] = PlayerAttribute.Control,
      ["Gathering"] = PlayerAttribute.Gathering,
      ["Perception"] = PlayerAttribute.Perception
    };

  /// <summary>The status every meal grants, read out of ItemAction.Data[0].</summary>
  private const uint WellFedStatusId = 48;

  /// <summary>
  /// On every equippable item, and useful only in combat -- so it must not make
  /// a combat food look relevant to a gatherer.
  /// </summary>
  private const uint VitalityParam = 3;

  /// <summary>
  /// Where loose food sits, read live. The armoury holds no consumables. The
  /// saddlebag is not read here: after a cold restart its live containers hold
  /// nothing until the saddlebag is opened, so it comes from ItemFinderModule
  /// instead (see <see cref="ReadSaddleBag"/>).
  /// </summary>
  private static readonly InventoryType[] Bags =
  [
    InventoryType.Inventory1,
    InventoryType.Inventory2,
    InventoryType.Inventory3,
    InventoryType.Inventory4
  ];

  /// <summary>The cache lists carry high quality as this offset on the item id.</summary>
  private const uint CachedHighQualityOffset = 1_000_000;

  public FoodReading Read()
  {
    if (!_clientState.IsLoggedIn) return Empty;

    try
    {
      (bool fed, float remaining, ushort param) = ReadWellFed();
      List<Held> held = ReadBags();
      FoodChoice? active = fed ? Active(param, held) : null;

      // Read once and shared: Best and the held-food ranking both need to know
      // which stats this job actually uses, and both should agree on it.
      HashSet<uint> relevant = RelevantParams();

      // GetAttribute reports whatever food is currently active baked into the
      // total (confirmed via probe), so multiplying by it while already fed
      // would double-count that food's own bonus on top of itself. Safe only
      // when there is nothing active to be counted twice.
      bool liveStats = !fed;

      // Retainer copies are only for saying where else a carried food is. They
      // stay out of held, so nothing suggests eating food that is not carried.
      List<Held> elsewhere = [];
      ReadRetainers(elsewhere);

      return new FoodReading(
        fed,
        remaining,
        active,
        Banked(held),
        held.Count == 0 ? null : Choose(held, relevant, liveStats),
        Stacks(held, elsewhere, relevant, active, liveStats));
    }
    catch (Exception ex)
    {
      // A panel that cannot read the bags should say nothing, not take the
      // window down with it.
      _logger.Error(ex, "[Food] Could not read food state.");
      return Empty;
    }
  }

  private static FoodReading Empty => new(false, 0, null, TimeSpan.Zero, null, []);

  /// <summary>The order /tm food lists the roles in.</summary>
  private static readonly string[] RoleOrder = ["DPS", "Healer", "Tank", "Crafter", "Gatherer"];

  public IReadOnlyList<RolePick> BestByRole()
  {
    if (!_clientState.IsLoggedIn) return [];

    try
    {
      List<Held> held = ReadBags();
      ReadRetainers(held);

      // Ranked by the caps alone, not by the equipped job's stats as Read()
      // does: this lists every role at once, and the job in hand says nothing
      // about the other four. Above a few hundred in a stat the cap is what a
      // meal actually gives, so it is the fair comparison.
      var ranked = held
        .Select((food) => (Food: food, Row: FoodRow(food.Item)))
        .Where((entry) => entry.Row is not null)
        .Select((entry) => (entry.Food, Classify(entry.Row).Role, Value: CapTotal(entry.Row!.Value, entry.Food.HighQuality)))
        .ToList();

      // OrderBy is stable and held lists the bags first, then the saddlebag,
      // then retainers -- so a meal held in more than one place is reported
      // where it is easiest to reach.
      return RoleOrder
        .Select((role) =>
        {
          var best = ranked
            .Where((entry) => entry.Role == role)
            .OrderByDescending((entry) => entry.Value)
            .ThenByDescending((entry) => entry.Food.HighQuality)
            .ThenBy((entry) => entry.Food.Item.Name.ToString(), StringComparer.Ordinal)
            .FirstOrDefault();

          return best.Food.Item.RowId == 0
            ? new RolePick(role, null, false, null)
            : new RolePick(role, best.Food.Item.Name.ToString(), best.Food.HighQuality, best.Food.Source);
        })
        .ToList();
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "[Food] Could not read food by role.");
      return [];
    }
  }

  /// <summary>
  /// Read from the journal (QuestManager.NormalQuests, the same list /tmwiki
  /// shows) and the Quest sheet, so it is the same answer the quest offer and
  /// the journal give, without touching either window. Thirty entries at most,
  /// so nothing is cached.
  ///
  /// Reward is the fixed list and can point at rows other than items, so only
  /// item rows are kept. OptionalItemReward is the list the player picks one of.
  /// </summary>
  public IReadOnlyList<QuestFood> FromAcceptedQuests()
  {
    if (!_clientState.IsLoggedIn) return [];

    try
    {
      List<QuestFood> quests = [];

      QuestManager* manager = QuestManager.Instance();
      if (manager is null) return quests;

      foreach (ref readonly var work in manager->NormalQuests)
      {
        if (work.QuestId == 0) continue;

        // QuestWork holds the id less 0x10000; the Quest sheet holds the full id.
        uint id = work.QuestId + 0x10000u;
        if (_dataManager.GetExcelSheet<QuestRow>().GetRowOrDefault(id) is not { } row) continue;

        List<QuestFoodItem> fixedFood = [];
        for (int i = 0; i < row.Reward.Count; i++)
          if (row.Reward[i].TryGetValue(out ItemRow item) && QuestMeal(item, row.ItemCountReward[i], false) is { } meal)
            fixedFood.Add(meal);

        List<QuestFoodItem> choiceFood = [];
        for (int i = 0; i < row.OptionalItemReward.Count; i++)
          if (row.OptionalItemReward[i].ValueNullable is { } item
              && QuestMeal(item, row.OptionalItemCountReward[i], row.OptionalItemIsHQReward[i]) is { } meal)
            choiceFood.Add(meal);

        if (fixedFood.Count + choiceFood.Count == 0) continue;

        quests.Add(new QuestFood(id, row.Name.ToString(), fixedFood, choiceFood));
      }

      return quests;
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "[Food] Could not read food from accepted quests.");
      return [];
    }
  }

  public bool IsMeal(uint itemId)
  {
    uint id = itemId % CachedHighQualityOffset;
    return id != 0 && _dataManager.GetExcelSheet<ItemRow>().GetRowOrDefault(id) is { } item && IsMeal(item);
  }

  private QuestFoodItem? QuestMeal(ItemRow item, byte quantity, bool hq)
    => item.RowId == 0 || !IsMeal(item)
      ? null
      : new QuestFoodItem(item.Name.ToString(), Math.Max(1, (int)quantity), hq, Classify(FoodRow(item)).Role);

  /// <summary>
  /// "1x Tempura Platter in Inventory\n3x Tempura Platter (HQ) in Saddlebag". The
  /// food is named on every line: a tooltip drawn over a list does not show
  /// which row it belongs to. Places keep the order they were read in -- bags,
  /// saddlebag, premium saddlebag, retainers -- NQ before HQ within each.
  /// </summary>
  private static string Where(string name, IEnumerable<Held> copies)
    => string.Join("\n", copies
      .GroupBy((f) => (f.Source, f.HighQuality))
      .Select((place) =>
        $"{place.Sum((f) => f.Quantity)}x {name}{(place.Key.HighQuality ? " (HQ)" : "")} in {place.Key.Source}"));

  /// <summary>Every cap a meal has, summed; a flat bonus counts as its value.</summary>
  private static int CapTotal(ItemFoodRow food, bool hq)
  {
    int total = 0;

    foreach (var entry in food.Params)
    {
      if (entry.BaseParam.RowId == 0) continue;
      total += entry.IsRelative ? (hq ? entry.MaxHQ : entry.Max) : (hq ? entry.ValueHQ : entry.Value);
    }

    return total;
  }

  /// <summary>
  /// One row per item id, grouped by quality since NQ and HQ of the same food
  /// can carry different bonuses.
  /// </summary>
  private List<FoodStack> Stacks(List<Held> held, List<Held> elsewhere, HashSet<uint> relevant, FoodChoice? active,
    bool liveStats)
  {
    return held
      .GroupBy((f) => (f.Item.RowId, f.HighQuality))
      .Select((group) =>
      {
        Held first = group.First();
        ItemFoodRow? food = FoodRow(first.Item);
        bool hq = group.Key.HighQuality;
        (FoodPursuit pursuit, string role) = Classify(food);
        bool isActive = active is not null && active.ItemId == first.Item.RowId && active.HighQuality == hq;

        return new FoodStack(
          first.Item.Name.ToString() + (hq ? " (HQ)" : ""),
          StatText(food, hq),
          pursuit,
          role,
          Evaluate(first, relevant, liveStats),
          hq,
          isActive,
          group.Sum((f) => f.Quantity),
          // Both qualities, as Allagan Tools lists them: the NQ row of a food
          // held HQ in the saddlebag otherwise looked like the saddlebag was empty.
          Where(first.Item.Name.ToString(),
            held.Concat(elsewhere).Where((f) => f.Item.RowId == group.Key.RowId)));
      })
      // What you're already eating leads its pursuit group -- you're benefiting
      // from it right now, whether or not it happens to be the top score.
      // Highest score next; ties broken by name so the ordering doesn't jitter
      // frame to frame among equal scores.
      .OrderBy((stack) => stack.Pursuit)
      .ThenByDescending((stack) => stack.IsActive)
      .ThenByDescending((stack) => stack.Score)
      .ThenBy((stack) => stack.Name, StringComparer.Ordinal)
      .ToList();
  }

  /// <summary>
  /// A food's bonuses, condensed for a fixed-width column: abbreviated stat
  /// name, the NQ value (the HQ value only ever differs by a point or two, and
  /// the cap is what actually binds at any real stat total), and the cap.
  /// </summary>
  private string StatText(ItemFoodRow? food, bool hq)
  {
    if (food is null) return "no stats";

    List<string> parts = [];

    for (int i = 0; i < food.Value.Params.Count; i++)
    {
      var entry = food.Value.Params[i];
      uint param = entry.BaseParam.RowId;
      if (param == 0) continue;

      int value = hq ? entry.ValueHQ : entry.Value;
      int cap = hq ? entry.MaxHQ : entry.Max;

      parts.Add(entry.IsRelative
        ? $"{FullParamName(param)} +{value}% (Cap {cap})"
        : $"{FullParamName(param)} +{value}");
    }

    return parts.Count == 0 ? "no stats" : string.Join(" | ", parts);
  }

  /// <summary>
  /// Which pursuit a food serves and which role its bonuses best match, both
  /// read from which stats it actually grants rather than guessed from its
  /// name. Tenacity and Piety appear on no other role's gear (see
  /// RelevantParams), so a food granting either belongs unambiguously to Tank
  /// or Healer; every other combat stat — Crit, Determination, Direct Hit,
  /// the two speeds — is shared across at least two roles, so it settles on
  /// DPS rather than picking one of the others arbitrarily.
  ///
  /// A meal with none of those gets no role (empty). Checked against every meal
  /// on 30 Oct 2026: Bacon Broth (desynthesis and durability), the eight
  /// resistance Drops, and Kingcake and Tailor-made Eel Pie (no stats at all)
  /// used to fall through to DPS.
  /// </summary>
  private (FoodPursuit Pursuit, string Role) Classify(ItemFoodRow? food)
  {
    bool sharedCombat = false;

    if (food is not null)
      foreach (var entry in food.Value.Params)
      {
        string name = FullParamName(entry.BaseParam.RowId);
        if (CraftingStats.Contains(name)) return (FoodPursuit.Crafting, "Crafter");
        if (GatheringStats.Contains(name)) return (FoodPursuit.Gathering, "Gatherer");
        if (name.Equals("Tenacity", StringComparison.OrdinalIgnoreCase)) return (FoodPursuit.Combat, "Tank");
        if (name.Equals("Piety", StringComparison.OrdinalIgnoreCase)) return (FoodPursuit.Combat, "Healer");
        if (SharedCombatStats.Contains(name)) sharedCombat = true;
      }

    return (FoodPursuit.Combat, sharedCombat ? "DPS" : "");
  }

  /// <summary>Vitality is left out: every combat meal grants it, so it says nothing about the role.</summary>
  private static readonly HashSet<string> SharedCombatStats =
    new(StringComparer.OrdinalIgnoreCase)
    {
      "Critical Hit", "Determination", "Direct Hit Rate", "Skill Speed", "Spell Speed"
    };

  private static readonly HashSet<string> CraftingStats =
    new(StringComparer.OrdinalIgnoreCase) { "Craftsmanship", "Control", "CP" };

  private static readonly HashSet<string> GatheringStats =
    new(StringComparer.OrdinalIgnoreCase) { "Gathering", "Perception", "GP" };

  private string FullParamName(uint id)
  {
    BaseParam? row = _dataManager.GetExcelSheet<BaseParam>().GetRowOrDefault(id);
    string name = row?.Name.ToString() ?? "";
    return name.Length > 0 ? name : $"param{id}";
  }

  /// <summary>
  /// Quality is carried in the status parameter as an offset rather than a flag:
  /// an HQ meal reports its row plus ten thousand.
  /// </summary>
  private const ushort HighQualityOffset = 10000;

  /// <summary>ItemFood row to the item that grants it. Built once, on demand.</summary>
  private Dictionary<ushort, uint>? _mealsByFoodRow;

  /// <summary>
  /// What is currently being digested.
  ///
  /// The status parameter is an **ItemFood row, not an item id** -- reading it as
  /// an item reported eating a Dated Poison Dagger, because Flatbread's food row
  /// is 114 and item 114 is a dagger. So it has to be resolved backwards through
  /// the meals that point at that row.
  ///
  /// Quantity is what is still in the bags, so zero is a real answer: it means
  /// that was the last one and the next meal has to be something else.
  /// </summary>
  private FoodChoice? Active(ushort param, List<Held> held)
  {
    if (param == 0) return null;

    bool hq = param > HighQualityOffset;
    ushort foodRow = (ushort)(hq ? param - HighQualityOffset : param);

    _mealsByFoodRow ??= BuildMealIndex();

    if (!_mealsByFoodRow.TryGetValue(foodRow, out uint id)) return null;

    ItemRow? item = _dataManager.GetExcelSheet<ItemRow>().GetRowOrDefault(id);
    if (item is null) return null;

    int remaining = held
      .Where((f) => f.Item.RowId == id && f.HighQuality == hq)
      .Sum((f) => f.Quantity);

    return new FoodChoice(id, item.Value.Name.ToString(), hq, remaining, Classify(FoodRow(item.Value)).Role);
  }

  private Dictionary<ushort, uint> BuildMealIndex()
  {
    Dictionary<ushort, uint> map = [];

    foreach (ItemRow item in _dataManager.GetExcelSheet<ItemRow>())
    {
      if (!IsMeal(item)) continue;

      Lumina.Excel.Sheets.ItemAction? action = item.ItemAction.ValueNullable;
      if (action is null || action.Value.Data.Count < 2) continue;

      ushort row = action.Value.Data[1];
      if (row != 0) map.TryAdd(row, item.RowId);
    }

    return map;
  }

  /// <summary>
  /// How long the bags could keep you fed. Each item's duration is read rather
  /// than assumed to be thirty minutes, so a food that ever differs is counted
  /// honestly.
  ///
  /// This is a **floor**. The free company action Meat and Mead adds five, ten
  /// or fifteen minutes to every food depending on its rank, and the sheet
  /// carries the base duration only -- so a thirty minute meal can really be
  /// forty-five. Detecting it would mean matching a status id we have not
  /// confirmed, and understating is the safe direction.
  /// </summary>
  private TimeSpan Banked(List<Held> held)
  {
    long seconds = 0;

    foreach (Held food in held)
    {
      Lumina.Excel.Sheets.ItemAction? action = food.Item.ItemAction.ValueNullable;
      if (action is null || action.Value.Data.Count < 3) continue;

      seconds += (long)action.Value.Data[2] * food.Quantity;
    }

    return TimeSpan.FromSeconds(seconds);
  }

  private (bool Fed, float Remaining, ushort Param) ReadWellFed()
  {
    BattleChara* player = Control.GetLocalPlayer();
    if (player is null) return (false, 0, 0);

    StatusManager* statuses = player->GetStatusManager();
    if (statuses is null) return (false, 0, 0);

    // A fixed sweep rather than a validity count: an off-by-one would silently
    // report "not fed" to someone who just ate.
    for (int i = 0; i < 30; i++)
      if (statuses->Status[i].StatusId == WellFedStatusId)
        return (true, statuses->Status[i].RemainingTime, statuses->Status[i].Param);

    return (false, 0, 0);
  }

  private List<Held> ReadBags()
  {
    List<Held> held = [];
    InventoryManager* manager = InventoryManager.Instance();
    if (manager is null) return held;

    foreach (InventoryType bag in Bags)
    {
      InventoryContainer* container = manager->GetInventoryContainer(bag);
      if (container is null) continue;

      for (int index = 0; index < container->Size; index++)
      {
        InventoryItem* slot = container->GetInventorySlot(index);
        if (slot is null || slot->ItemId == 0) continue;

        ItemRow? item = _dataManager.GetExcelSheet<ItemRow>().GetRowOrDefault(slot->ItemId);
        if (item is null || !IsMeal(item.Value)) continue;

        held.Add(new Held(
          item.Value,
          slot->Quantity,
          slot->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality),
          "Inventory"));
      }
    }

    ReadSaddleBag(held);
    return held;
  }

  /// <summary>
  /// The saddlebag and premium saddlebag, from ItemFinderModule -- the game's
  /// Search for Item cache, which is full from login and follows every item
  /// move. HaselDebug showed the live SaddleBag containers empty after a cold
  /// restart (26 Oct 2026), which made saddlebag food look absent.
  /// </summary>
  private void ReadSaddleBag(List<Held> held)
  {
    var finder = FFXIVClientStructs.FFXIV.Client.UI.Misc.ItemFinderModule.Instance();
    if (finder is null || !finder->IsSaddleBagCached) return;

    AddCached(held, finder->SaddleBagItemIds, finder->SaddleBagItemCount, "Saddlebag");
    AddCached(held, finder->PremiumSaddleBagItemIds, finder->PremiumSaddleBagItemCount, "Premium Saddlebag");
  }

  /// <summary>
  /// Every retainer, from the same ItemFinderModule cache: full from login, and
  /// brought up to date whenever a retainer's inventory is opened. Only
  /// <see cref="BestByRole"/> reads this -- the meal a panel suggests eating now
  /// has to be one you are carrying.
  ///
  /// Names come from RetainerManager, which may not be filled until a summoning
  /// bell is used; until then the retainer is named generically.
  /// </summary>
  private void ReadRetainers(List<Held> held)
  {
    var finder = FFXIVClientStructs.FFXIV.Client.UI.Misc.ItemFinderModule.Instance();
    if (finder is null) return;

    Dictionary<ulong, string> names = [];
    RetainerManager* retainers = RetainerManager.Instance();
    if (retainers is not null && retainers->IsReady)
      foreach (ref readonly var retainer in retainers->Retainers)
        if (retainer.RetainerId != 0) names[retainer.RetainerId] = retainer.NameString;

    foreach (var (id, inventory) in finder->RetainerInventories)
    {
      if (inventory.Value is null) continue;
      AddCached(held, inventory.Value->ItemIds, inventory.Value->ItemCount, names.GetValueOrDefault(id, "a retainer"));
    }
  }

  private void AddCached(List<Held> held, Span<uint> ids, Span<ushort> counts, string source)
  {
    for (int index = 0; index < ids.Length; index++)
    {
      uint id = ids[index];
      if (id == 0) continue;

      ItemRow? item = _dataManager.GetExcelSheet<ItemRow>().GetRowOrDefault(id % CachedHighQualityOffset);
      if (item is null || !IsMeal(item.Value)) continue;

      held.Add(new Held(item.Value, counts[index], id >= CachedHighQualityOffset, source));
    }
  }

  /// <summary>
  /// Identified by its UI category rather than a hardcoded id, so a sheet
  /// reshuffle cannot silently empty the panel.
  /// </summary>
  private static bool IsMeal(ItemRow item)
    => (item.ItemUICategory.ValueNullable?.Name.ToString() ?? "")
       .Contains("Meal", StringComparison.OrdinalIgnoreCase);

  /// <summary>
  /// Best relevant food if there is one, otherwise the most disposable thing in
  /// the bags. When only the experience bonus is on offer the goal inverts --
  /// recommending someone eat their only HQ meal for an experience tick is worse
  /// advice than saying nothing.
  /// </summary>
  private FoodChoice Choose(List<Held> held, HashSet<uint> relevant, bool liveStats)
  {
    List<(Held Food, int Value)> useful = [];

    foreach (Held food in held)
    {
      int value = Evaluate(food, relevant, liveStats);
      if (value > 0) useful.Add((food, value));
    }

    if (useful.Count > 0)
      return Choice(useful
        .OrderByDescending((entry) => entry.Value)
        .ThenByDescending((entry) => entry.Food.HighQuality)
        .First().Food);

    Held junk = held
      .OrderBy((f) => f.Item.LevelItem.RowId)
      .ThenBy((f) => f.HighQuality)
      .ThenByDescending((f) => f.Quantity)
      .First();

    return Choice(junk);
  }

  private static FoodChoice Choice(Held food)
    => new(food.Item.RowId, food.Item.Name.ToString(), food.HighQuality, food.Quantity);

  /// <summary>
  /// Which stats this class actually uses, taken from what the player's own gear
  /// grants rather than a role table. Tenacity appears only on tank gear, Piety
  /// only on healer gear, Gathering only on gatherer gear -- so the equipped set
  /// describes the class better than any list we could maintain, and stays
  /// correct for jobs added later.
  /// </summary>
  private HashSet<uint> RelevantParams()
  {
    HashSet<uint> relevant = [];

    InventoryManager* manager = InventoryManager.Instance();
    InventoryContainer* gear = manager is null
      ? null
      : manager->GetInventoryContainer(InventoryType.EquippedItems);

    if (gear is null) return relevant;

    for (int index = 0; index < gear->Size; index++)
    {
      InventoryItem* slot = gear->GetInventorySlot(index);
      if (slot is null || slot->ItemId == 0) continue;

      ItemRow? item = _dataManager.GetExcelSheet<ItemRow>().GetRowOrDefault(slot->ItemId);
      if (item is null) continue;

      for (int i = 0; i < item.Value.BaseParam.Count; i++)
      {
        uint param = item.Value.BaseParam[i].RowId;
        if (param != 0 && item.Value.BaseParamValue[i] != 0) relevant.Add(param);
      }
    }

    // Vitality is granted by everything and useful only in combat. Dropping it
    // for crafters and gatherers stops a combat meal reading as relevant.
    if (IsHandOrLand()) relevant.Remove(VitalityParam);

    return relevant;
  }

  private bool IsHandOrLand()
  {
    PlayerState* state = PlayerState.Instance();
    if (state is null) return false;

    return _dataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>()
      .GetRowOrDefault(state->CurrentClassJobId)?.DohDolJobIndex >= 0;
  }

  /// <summary>
  /// What a food is worth right now: min(stat * percent, cap), summed over the
  /// stats this class uses.
  ///
  /// <paramref name="liveStats"/> gates whether stat is the player's real,
  /// current total (via IPlayerState.GetAttribute) or just falls back to the
  /// cap. Live stats are only safe to use unfed -- GetAttribute already
  /// includes whatever food is currently active, so multiplying by it while
  /// fed would count that food's own bonus twice. While fed this falls back to
  /// the cap directly, which above a few hundred in a stat is the binding
  /// constraint anyway, and overstates the bonus only for a low-level
  /// character choosing between foods nobody is really choosing between.
  ///
  /// Used for ordering only. Which stats it grants is not shown: the panel exists
  /// to say you are not fed and name something to eat, and nobody choosing
  /// between meals in their own bags is weighing two points of Skill Speed.
  /// </summary>
  private int Evaluate(Held food, HashSet<uint> relevant, bool liveStats)
  {
    ItemFoodRow? effect = FoodRow(food.Item);
    if (effect is null) return 0;

    int total = 0;

    foreach (var entry in effect.Value.Params)
    {
      uint param = entry.BaseParam.RowId;
      if (param == 0 || !relevant.Contains(param)) continue;

      int amount = food.HighQuality ? entry.ValueHQ : entry.Value;
      int cap = food.HighQuality ? entry.MaxHQ : entry.Max;
      if (amount <= 0) continue;

      int granted;

      if (!entry.IsRelative)
      {
        granted = amount;
      }
      else if (liveStats && AttributeByParamName.TryGetValue(FullParamName(param), out PlayerAttribute attribute))
      {
        int stat = _playerState.GetAttribute(attribute);
        granted = Math.Min((int)(stat * (amount / 100.0)), cap);
      }
      else
      {
        granted = cap;
      }

      if (granted <= 0) continue;

      total += granted;
    }

    return total;
  }

  private ItemFoodRow? FoodRow(ItemRow item)
  {
    Lumina.Excel.Sheets.ItemAction? action = item.ItemAction.ValueNullable;
    if (action is null || action.Value.Data.Count < 2) return null;

    ushort row = action.Value.Data[1];
    return row == 0 ? null : _dataManager.GetExcelSheet<ItemFoodRow>().GetRowOrDefault(row);
  }

  private readonly record struct Held(ItemRow Item, int Quantity, bool HighQuality, string Source);
}
