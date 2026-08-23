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
/// quest-patches.json — all 35 are present and their patches agree. They are
/// ordinary quest ids, not magic numbers.
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
    // The daily bills also need Second Lieutenant; the rank is checked
    // separately. Elite and Dangerous is what opens the weekly B-rank bills.
    ["huntDaily"] = [67099, 67100, 67101],  // Let the Hunt Begin, per Grand Company
    ["eliteHunt"] = [67658],                // Elite and Dangerous

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

    // Zodiac is deliberately absent. "A Relic Reborn" is per weapon — thirteen
    // separate quests with no shared entry — so there is no single id that means
    // "has started the Zodiac line", and Morbid Motivation stays ungated rather
    // than gated on an arbitrary one.
  };
}
