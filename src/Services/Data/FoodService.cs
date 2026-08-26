using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using PlayerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;
using PlayerAttribute = Dalamud.Game.Player.PlayerAttribute;
using ItemFoodRow = Lumina.Excel.Sheets.ItemFood;
using ItemRow = Lumina.Excel.Sheets.Item;

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

public record FoodChoice(uint ItemId, string Name, bool HighQuality, int Quantity);

/// <summary>
/// One food item's stat bonuses, which pursuit it serves, the role its
/// bonuses best match (for colour coding), and a ranking within its pursuit —
/// value to the currently equipped job, the same cap-first reasoning
/// FoodService uses to pick a single Best. Job-relevant rather than a raw
/// stat total: Combat isn't one audience the way Crafting and Gathering are,
/// so a food full of Skill Speed still needs to rank behind a Piety food for
/// someone playing a healer.
/// </summary>
public record FoodStack(string Name, string Stats, FoodPursuit Pursuit, string Role, int Score, bool HighQuality,
  bool IsActive, int Quantity);

/// <summary>What kind of play a food's stats are for — the axis food actually splits on in this game.</summary>
public enum FoodPursuit { Combat, Crafting, Gathering }

public interface IFoodService
{
  /// <summary>
  /// Reads bags and status. Cheap enough to call when a window opens; not
  /// cached, because both inputs change while the window is open.
  /// </summary>
  FoodReading Read();
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

  /// <summary>Where loose food sits. The armoury holds no consumables.</summary>
  private static readonly InventoryType[] Bags =
  [
    InventoryType.Inventory1,
    InventoryType.Inventory2,
    InventoryType.Inventory3,
    InventoryType.Inventory4,
    InventoryType.SaddleBag1,
    InventoryType.SaddleBag2
  ];

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

      return new FoodReading(
        fed,
        remaining,
        active,
        Banked(held),
        held.Count == 0 ? null : Choose(held, relevant, liveStats),
        Stacks(held, relevant, active, liveStats));
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

  /// <summary>
  /// One row per item id, grouped by quality since NQ and HQ of the same food
  /// can carry different bonuses.
  /// </summary>
  private List<FoodStack> Stacks(List<Held> held, HashSet<uint> relevant, FoodChoice? active, bool liveStats)
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
          group.Sum((f) => f.Quantity));
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
  /// </summary>
  private (FoodPursuit Pursuit, string Role) Classify(ItemFoodRow? food)
  {
    if (food is not null)
      foreach (var entry in food.Value.Params)
      {
        string name = FullParamName(entry.BaseParam.RowId);
        if (CraftingStats.Contains(name)) return (FoodPursuit.Crafting, "Crafter");
        if (GatheringStats.Contains(name)) return (FoodPursuit.Gathering, "Gatherer");
        if (name.Equals("Tenacity", StringComparison.OrdinalIgnoreCase)) return (FoodPursuit.Combat, "Tank");
        if (name.Equals("Piety", StringComparison.OrdinalIgnoreCase)) return (FoodPursuit.Combat, "Healer");
      }

    return (FoodPursuit.Combat, "DPS");
  }

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

    return new FoodChoice(id, item.Value.Name.ToString(), hq, remaining);
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
          slot->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality)));
      }
    }

    return held;
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

  private readonly record struct Held(ItemRow Item, int Quantity, bool HighQuality);
}
