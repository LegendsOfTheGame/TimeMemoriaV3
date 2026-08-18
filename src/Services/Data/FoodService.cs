using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using PlayerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState;
using ItemFoodRow = Lumina.Excel.Sheets.ItemFood;
using ItemRow = Lumina.Excel.Sheets.Item;

namespace TimeMemoria.Services;

/// <summary>What the player is holding, and whether they are already fed.</summary>
/// <param name="WellFed">Whether the well-fed status is currently active.</param>
/// <param name="RemainingSeconds">How long that has left. Zero when not fed.</param>
/// <param name="StacksHeld">Food stacks across bags and saddlebag.</param>
/// <param name="Banked">
/// Total well-fed time sitting unused in the bags — every item's own duration
/// times how many are held. The point of the panel in one number.
/// </param>
/// <param name="Best">The suggestion, or null when there is no food at all.</param>
public record FoodReading(bool WellFed, float RemainingSeconds, int StacksHeld, TimeSpan Banked, FoodChoice? Best);

/// <param name="Effect">
/// What it grants, already capped against live stats — empty when nothing it
/// grants applies to the current class, which is a suggestion for the
/// experience bonus alone.
/// </param>
public record FoodChoice(uint ItemId, string Name, bool HighQuality, int Quantity, string Effect);

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
public unsafe class FoodService(ILogger _logger, IDataManager _dataManager, IClientState _clientState) : IFoodService
{
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
      (bool fed, float remaining) = ReadWellFed();
      List<Held> held = ReadBags();

      return new FoodReading(
        fed,
        remaining,
        held.Count,
        Banked(held),
        held.Count == 0 ? null : Choose(held));
    }
    catch (Exception ex)
    {
      // A panel that cannot read the bags should say nothing, not take the
      // window down with it.
      _logger.Error(ex, "[Food] Could not read food state.");
      return Empty;
    }
  }

  private static FoodReading Empty => new(false, 0, 0, TimeSpan.Zero, null);

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

  private (bool Fed, float Remaining) ReadWellFed()
  {
    BattleChara* player = Control.GetLocalPlayer();
    if (player is null) return (false, 0);

    StatusManager* statuses = player->GetStatusManager();
    if (statuses is null) return (false, 0);

    // A fixed sweep rather than a validity count: an off-by-one would silently
    // report "not fed" to someone who just ate.
    for (int i = 0; i < 30; i++)
      if (statuses->Status[i].StatusId == WellFedStatusId)
        return (true, statuses->Status[i].RemainingTime);

    return (false, 0);
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
  private FoodChoice Choose(List<Held> held)
  {
    HashSet<uint> relevant = RelevantParams();

    List<(Held Food, int Value, string Effect)> useful = [];

    foreach (Held food in held)
    {
      (int value, string effect) = Evaluate(food, relevant);
      if (value > 0) useful.Add((food, value, effect));
    }

    if (useful.Count > 0)
    {
      (Held best, _, string effect) = useful
        .OrderByDescending((entry) => entry.Value)
        .ThenByDescending((entry) => entry.Food.HighQuality)
        .First();

      return Choice(best, effect);
    }

    Held junk = held
      .OrderBy((f) => f.Item.LevelItem.RowId)
      .ThenBy((f) => f.HighQuality)
      .ThenByDescending((f) => f.Quantity)
      .First();

    return Choice(junk, "");
  }

  private static FoodChoice Choice(Held food, string effect)
    => new(food.Item.RowId, food.Item.Name.ToString(), food.HighQuality, food.Quantity, effect);

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
  /// stats this class uses. Above a few hundred in a stat the cap is always what
  /// binds, so in practice this ranks by cap -- but the multiplication still
  /// matters for a low-level character, where it does not.
  /// </summary>
  private (int Value, string Effect) Evaluate(Held food, HashSet<uint> relevant)
  {
    ItemFoodRow? effect = FoodRow(food.Item);
    if (effect is null) return (0, "");

    int total = 0;
    List<string> parts = [];

    foreach (var entry in effect.Value.Params)
    {
      uint param = entry.BaseParam.RowId;
      if (param == 0 || !relevant.Contains(param)) continue;

      int amount = food.HighQuality ? entry.ValueHQ : entry.Value;
      int cap = food.HighQuality ? entry.MaxHQ : entry.Max;
      if (amount <= 0) continue;

      // Relative values are a percentage of a live stat total, capped. Those
      // totals are not readable yet -- gear sums miss a base of several hundred
      // -- so the cap is used directly. Above a few hundred in a stat that is
      // the binding constraint anyway, and every food measured on a level 61
      // gatherer was cap-bound. It overstates the bonus for a low-level
      // character, which changes the ordering only among foods nobody is
      // choosing between.
      int granted = entry.IsRelative ? cap : amount;
      if (granted <= 0) continue;

      total += granted;
      parts.Add($"{ParamName(param)} +{granted}");
    }

    return (total, string.Join(", ", parts));
  }

  private ItemFoodRow? FoodRow(ItemRow item)
  {
    Lumina.Excel.Sheets.ItemAction? action = item.ItemAction.ValueNullable;
    if (action is null || action.Value.Data.Count < 2) return null;

    ushort row = action.Value.Data[1];
    return row == 0 ? null : _dataManager.GetExcelSheet<ItemFoodRow>().GetRowOrDefault(row);
  }

  private string ParamName(uint id)
    => _dataManager.GetExcelSheet<Lumina.Excel.Sheets.BaseParam>()
         .GetRowOrDefault(id)?.Name.ToString() ?? "";

  private readonly record struct Held(ItemRow Item, int Quantity, bool HighQuality);
}
