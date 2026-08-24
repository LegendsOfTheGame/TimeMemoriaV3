namespace TimeMemoria.Services;

/// <summary>
/// Which features a character has actually unlocked, for the Adventurer's Ledger.
///
/// The ledger hides routines a character cannot do. Until now every gate it had
/// was an approximation: "has reached patch 5.0" standing in for "has completed
/// Fantastic Mr. Faux", because a main scenario patch was the only progress
/// figure the export carried. That is wrong in both directions — it shows Faux
/// Hollows to someone twenty quests into Shadowbringers, and there is no patch
/// number at all that expresses "owns a Gold Saucer pass".
///
/// Quest completion is the real rule, and it is free to read:
/// QuestManager.IsQuestComplete takes a raw quest id and answers from memory.
///
/// Every id below was taken from the wiki's own Unlock section for the feature,
/// via the id-gt field, and then checked against this plugin's own
/// quest-patches.json — every one is present and their patches agree. They are
/// ordinary quest ids, not magic numbers. A count is deliberately not quoted
/// here; it goes stale the moment a key is added and proves nothing the check
/// itself doesn't.
///
/// Several unlocks list more than one id. Those are starting-city or Grand
/// Company variants of the same quest; a character only ever completes one, so
/// the test is any-of, never all-of.
/// </summary>
public static class LedgerUnlocks
{
  /// <summary>
  /// Grand Company rank at which Adventurer Squadrons, the Hunt and player
  /// housing open up.
  ///
  /// This is the GrandCompanyRank sheet row id, which is also what
  /// IPlayerState.GetGrandCompanyRank returns — verified in game, where a
  /// Second Lieutenant reported exactly 9 against a 20-row sheet.
  ///
  /// Row 9 was identified by two columns that each single it out — MaxSeals
  /// 50,000 and RequiredHuntingLogRank 2 — and NOT by name. The sheet has no
  /// name column at all; names live in six GCRank*Text sheets, one per city and
  /// gender, and they infix the company word: "Second Storm Lieutenant" for
  /// Maelstrom, differently again for the Twin Adder. Any name match would have
  /// been wrong on at least two thirds of characters.
  /// </summary>
  public const byte SecondLieutenantRank = 9;

  /// <summary>
  /// Feature key as the ledger will read it, against the quest ids that unlock it.
  /// Key names match the ledger's own seedKeys where one already exists.
  /// </summary>
  public static readonly IReadOnlyDictionary<string, uint[]> Quests = new Dictionary<string, uint[]>
  {
    // --- Gold Saucer ------------------------------------------------------
    // The Gold Saucer itself, then the three attractions the ledger tracks
    // separately, because each is its own quest inside it.
    ["goldSaucer"] = [65970],           // It Could Happen to You
    ["miniCactpot"] = [66024],          // Scratch It Rich
    ["jumboCactpot"] = [66025],         // Hitting the Cactpot
    ["fashionReport"] = [68617],        // Passion for Fashion

    // --- Everyday features ------------------------------------------------
    ["challengeLog"] = [66967],         // Rising to the Challenge
    ["treasureHunt"] = [66747],         // Treasures and Tribulations
    ["deepDungeon"] = [67092],          // The House That Death Built
    ["retainerVenture"] = [66968, 66969, 66970],  // An Ill-conceived Venture, per city
    ["pvp"] = [66640, 66641, 66642],              // A Pup No Longer, per Grand Company
    ["leves"] = [65756, 66223, 66229],            // first levequest, per city

    // --- The Hunt ---------------------------------------------------------
    // Five hunts, not one, and the ledger has a daily row and a weekly B-rank
    // row for each. Every expansion set is four quests — three daily bill tiers
    // then an elite one — so the daily row takes the first of its set and the
    // B-rank row takes the last. Only the middle two are unrepresented, because
    // the ledger does not split its daily row by bill tier.
    //
    // A Realm Reborn is the exception: Let the Hunt Begin opens the daily bills
    // AND the weekly B-rank bill together, per that quest's own walkthrough, so
    // both ARR rows read huntDaily. It also needs Second Lieutenant, but the
    // rank is a requirement of the quest, so completing it proves the rank.
    //
    // eliteHunt is Heavensward, despite the unqualified name. Elite and
    // Dangerous is level 60, patch 3.0, and its page says outright that it
    // unlocks Heavensward elite marks — it was never the ARR quest the ledger
    // was reading it as. The name stays: builds already in the wild send it,
    // and its value has not changed, only which row should be asking.
    ["huntDaily"] = [67099, 67100, 67101],  // Let the Hunt Begin, per Grand Company
    ["eliteHunt"] = [67658],                // Elite and Dangerous — Heavensward
    ["clanHunt"] = [67655],                 // Let the Clan Hunt Begin
    ["veteranHunt"] = [68472],              // One-star Veteran Clan Hunt
    ["veteranHuntElite"] = [68475],         // Elite Veteran Clan Hunt
    ["nutsyHunt"] = [69133],                // Nuts to You
    ["nutsyHuntElite"] = [69136],           // Too Many Nutters
    ["guildshipHunt"] = [69712],            // The Hunt for Specimens
    ["guildshipHuntElite"] = [69715],       // Perfect Specimens
    ["dawnHunt"] = [70545],                 // A New Dawn, a New Hunt
    ["dawnHuntElite"] = [70548],            // The Hunt Goes On

    // --- Weekly content ---------------------------------------------------
    ["wondrousTails"] = [67928],        // Keeping Up with the Aliapohs
    ["fauxHollows"] = [69501],          // Fantastic Mr. Faux
    ["customDelivery"] = [67087],       // Arms Wide Open — Zhloe, the earliest client
    ["domanEnclave"] = [68622],         // Precious Reclamation
    ["islandSanctuary"] = [70179],      // Seeking Sanctuary
    ["cosmicExploration"] = [70789],    // A Cosmic Homecoming

    // --- Field operations -------------------------------------------------
    // Three separate unlocks, not one. The ledger's "Field Operations" covers
    // all of them, so any one is enough for that row — but the relic dailies
    // hang off Bozja and Occult Crescent specifically.
    ["eureka"] = [68614],               // And We Shall Call It Eureka
    ["bozja"] = [69370],                // Hail to the Queen
    ["occultCrescent"] = [70845],       // One Last Hurrah

    // --- Relic and limited-job dailies ------------------------------------
    ["blueMage"] = [68728],             // Out of the Blue
    ["maskedCarnivale"] = [68734],      // The Real Folk Blues
    ["anima"] = [67750],                // Coming into Its Own
    ["yorha"] = [69253],                // A Scandal in Komra

    // Zodiac was left out on the reasoning that "A Relic Reborn" is per weapon —
    // thirteen quests with no shared entry — so nothing means "has started the
    // Zodiac line". That much is true and stays true. It was the wrong question.
    // The ledger's row is Morbid Motivation, the repeatable Mysterious Map
    // turn-in, and the line converges well before it: all thirteen feed Up in
    // Arms, then Trials of the Braves, Celestial Radiance, and One Man's Trash,
    // which is Morbid Motivation's immediate prerequisite and one id.
    //
    // Gated on One Man's Trash rather than on Morbid Motivation itself, which is
    // repeatable: "has completed it" would hide the row from everyone who
    // unlocked the maps and has not yet handed one in — precisely the people the
    // row exists for.
    ["zodiacMaps"] = [66676],           // One Man's Trash

    // --- Allied societies -------------------------------------------------
    // Not per tribe: the wiki is explicit that no allied society quest opens
    // until this one is done, "even if your character is over level". The
    // ledger's level 41 floor was reading the level half of that and missing
    // the quest half, so a level 41 character who had not touched the main
    // scenario was still offered the row.
    ["alliedSociety"] = [66488],        // In Pursuit of the Past

    // --- Raid content -----------------------------------------------------
    // Glory Incarnate opens both the normal fourth floor and the whole Savage
    // tier — Savage needs no prior Savage clear, only this quest. The Ultimate
    // is deliberately absent: it wants an M4 (Savage) *clear*, which is a duty
    // result and not something this export reads.
    ["arcadion"] = [70976],             // Glory Incarnate
    ["windurst"] = [71015],             // The Hollow Promise
  };
}
