# Time Memoria → Adventurer's Ledger clipboard export

Contract for the payload written by **Copy for Adventurer's Ledger** on the
Progression tab.

- **Transport:** system clipboard only. The plugin makes no outbound requests.
- **Trigger:** user presses the button. Never automatic.
- **Direction:** one-way, plugin → ledger.

Everything below is **implemented and verified in Time Memoria 3.0.1** unless a
row says otherwise.

---

## Payload

Real output for *Haurche Greystone* (Mateus).

```json
{
  "source": "time-memoria",
  "version": "3.0.1.0",
  "exported": "2026-08-04T03:11:22.4180000Z",
  "name": "Haurche Greystone",
  "server": {
    "pdc": "North America",
    "ldc": "Crystal",
    "world": "Mateus"
  },
  "comm": 831,
  "playtime": {
    "days": 36,
    "hours": 13,
    "asOf": "2026-08-04T02:49:07.1120000Z"
  },
  "combat": {
    "Paladin": 86.505,
    "Warrior": 32.283,
    "Dark Knight": 0,
    "Gunbreaker": 0,
    "White Mage": 91.853,
    "Scholar": 100,
    "Astrologian": 0,
    "Sage": 0,
    "Monk": 0,
    "Dragoon": 0,
    "Ninja": 0,
    "Samurai": 0,
    "Reaper": 0,
    "Viper": 0,
    "Beastmaster": 0,
    "Bard": 100,
    "Machinist": 0,
    "Dancer": 71.218,
    "Black Mage": 0,
    "Summoner": 100,
    "Red Mage": 0,
    "Pictomancer": 0,
    "Blue Mage": 5.283
  },
  "craft": {
    "Carpenter": 100,
    "Blacksmith": 66.007,
    "Armorer": 62.344,
    "Goldsmith": 62.966,
    "Leatherworker": 62.582,
    "Weaver": 62.433,
    "Alchemist": 65.557,
    "Culinarian": 71.458
  },
  "gather": {
    "Miner": 90.422,
    "Botanist": 100,
    "Fisher": 84.988
  },
  "msqBreakdown": {
    "arr": 240,
    "hw": 138,
    "stb": 162,
    "shb": 157,
    "ew": 155,
    "dt": 125
  },
  "msqPatch": {
    "cleared": "7.2",
    "reached": "7.3"
  }
}
```

---

## `quests` — completion per journal section

```json
"quests": {
  "overall": 901, "msq": 583, "era": 19,
  "side": 148, "allied": 20, "class": 140, "leve": 51
}
```

| Key | Journal section |
|---|---|
| `msq` | Main Scenario |
| `era` | Chronicles of a New Era |
| `side` | Sidequests |
| `allied` | Allied Society Quests |
| `class` | Class & Job Quests |
| `leve` | Levequests |

**`overall` is the sum of those six, not the plugin's own Overall.** The plugin
has a seventh section, **Other Quests**, that the ledger has no bucket for.
Sending our Overall would include it and break the ledger's own
"sub-categories sum to Overall" check.

Counts ignore the Settings toggles for excluding Other Quests and Levequests.
Those change what the plugin displays; they are not a claim about what the
character has done, and a display preference must not rewrite stored data.

`questTotals` stays ledger-side. Our denominators are character-filtered — the
starting city, class and Grand Company routes not taken are excluded — so two
characters legitimately have different totals, and ours would not match a static
reference set.

---

## `msqPatch` — story position as a patch

For the ledger's **MSQ Progress** field, and for gating routines on story
milestones. This is how players actually describe their position — "I'm on
6.3" — rather than a quest count.

| Key | Meaning |
|---|---|
| `cleared` | Last patch whose **closing** MSQ quest is complete |
| `reached` | Last patch whose **opening** MSQ quest is complete |

Both require the quest to be **complete**, not merely accepted. `reached: "7.3"`
means 7.3's opening quest is finished and you are inside that patch's content.

`reached` can be one patch ahead of `cleared`: a character who has finished 7.3's
opening quest but not its last one reports `cleared: "7.2", reached: "7.3"`.

**Which to use depends on what the field means to you.**

- If it mirrors what a player would type for "where am I in the story", use
  `reached`. That is the natural reading, and it matches values already entered
  by hand — switching such a field to `cleared` would roll existing users
  backwards by up to a patch on their first import.
- If it gates content that must not appear early, `cleared` is the strict floor.

Pandora Lunar uses `reached`, because its `c.patch` field was hand-entered with
the first meaning.

Either key may be absent, and the whole object is omitted if both are. A brand
new character sends no `msqPatch` at all. **Never store an absent value over one
you already hold** — same rule as `playtime`.

### Known ceiling

The patch boundaries stop at the last patch recorded in `toc.json`, currently
**7.5** — which is also the newest patch to add any Main Scenario, since 7.55
added none. So there is no gap at present.

The ceiling still matters on the day a patch lands: until the new bookends are
added, a character who has finished it reports the previous patch. **It is always
a floor, never an overstatement** — so an importer should never write a lower
value over a higher stored one. Take the greater of the two.

Values are strings, not numbers — `"7.2"`, not `7.2` — because `7.10` will
eventually exist and would sort and compare wrongly as a float. Compare by
splitting on the dot, or map to an ordinal.

---

## `unlocks` — which features this character actually has

```json
"unlocks": {
  "goldSaucer": true, "miniCactpot": true, "jumboCactpot": true,
  "fashionReport": true, "challengeLog": true, "treasureHunt": true,
  "deepDungeon": false, "retainerVenture": true, "pvp": false, "leves": true,
  "huntDaily": true, "eliteHunt": true, "wondrousTails": true,
  "fauxHollows": false, "customDelivery": true, "domanEnclave": true,
  "islandSanctuary": false, "cosmicExploration": false,
  "eureka": true, "bozja": false, "occultCrescent": false,
  "blueMage": false, "maskedCarnivale": false, "anima": true, "yorha": false,
  "grandCompany": true, "squadron": true
}
```

**This is what `requires` was always approximating.** A main scenario patch was
the only progress figure the export carried, so the ledger had to express
"completed Fantastic Mr. Faux" as "reached patch 5.0". That is wrong in both
directions — it shows Faux Hollows to someone twenty quests into Shadowbringers,
and no patch number at all can express "owns a Gold Saucer pass".

Every key but the last two is `QuestManager.IsQuestComplete` over the quest that
unlocks the feature, read from memory at export time. The ids came from the
wiki's Unlock sections and were checked against the plugin's own
`quest-patches.json` — all 35 present, patches agreeing. Where an unlock lists
several ids they are starting-city or Grand Company variants of one quest, and
the test is any-of.

`grandCompany` is enlistment. `squadron` is Grand Company rank ≥ 9 — Second
Lieutenant, the rank that opens Adventurer Squadrons, the daily Hunt bills and
player housing, so one read gates several rows.

**Absent means "this build did not know about that unlock", never false.** Fall
back to your own patch gate for a missing key. If absent and false meant the same
thing, adding a key in a later version would read as every character suddenly
unlocking that feature.

The whole object is omitted when the player is not loaded, since a payload of
falses would tell the ledger to hide everything.

### Not covered

`zodiac`. "A Relic Reborn" is per weapon — thirteen quests with no shared entry —
so no single id means "has started the Zodiac line". Anything gated on it (Morbid
Motivation) stays ungated rather than gated on an arbitrary one.

---

## Level encoding

`level + (currentExp ÷ expRequiredForThisLevel)`, rounded to **3 decimals**.

| Job | Level | Exp | Encoded |
|---|---|---|---|
| Paladin | 86 | 4,665,561 / 9,231,000 | `86.505` |
| Fisher | 84 | 7,853,871 / 7,948,000 | `84.988` |

- **Max level emits a bare integer** — `100`, never `100.0` or `101`. The game
  keeps reporting an experience requirement at the ceiling instead of zero, so
  max is detected from the character's level cap.
- **`0` is ambiguous** — unlocked-but-unlevelled and never-unlocked are
  indistinguishable in game data.
- **Summoner and Scholar share one experience slot** and always report identical
  values. This is correct, not duplication.
- **Beastmaster ships before it is playable** (8 Sep 2026). Already in the game's
  data, exports as `0` until then.

---

## Field status

| Field | Source | Status |
|---|---|---|
| `combat` / `craft` / `gather` | `IPlayerState.GetClassJobLevel` + `GetClassJobExperience`, `ParamGrow.ExpToNext` | **Built.** Verified job-by-job against the in-game Character panel |
| `name` | `IPlayerState.CharacterName` | **Built** |
| `server.world` | `IPlayerState.HomeWorld` | **Built** |
| `server.ldc` | `World → WorldDCGroupType.Name` | **Built** |
| `server.pdc` | `WorldDCGroupType → WorldRegionGroup.Name` | **Built** |
| `comm` | `IPlayerState.PlayerCommendations` | **Built** |
| `playtime` | Parsed from the `/playtime` system message | **Built.** Stale by nature — see below |
| `msqBreakdown` | Filtered MSQ bucket counts per expansion | **Built.** ARR correctly reports 240, not the 289 raw rows, so starting-city and Grand Company filtering is applied |
| `msqPatch` | `toc.json` patch bookends vs `QuestManager.IsQuestComplete` | **Built.** Feeds the MSQ Progress field — see section above. Pandora Lunar consumes `reached` |
| `msqBreakdownTotals` | — | **Deliberately not sent.** Static reference data the ledger already holds, and ours understates Dawntrail |
| `quests` | Top-level journal section completion | **Built.** Six sections plus `overall` — see section below |
| `questTotals` | — | **Deliberately not sent.** Our denominators are character-filtered and would not match the ledger's static ones |

### Not available from the plugin

`duty`, `tradeCollected`, `tradeMade`, `routines`, `notes`, `custom`,
`jobQuestsDone`. These stay ledger-side and must not be touched by an import.

`societies` rank/points sits in game memory but has not been investigated.

---

## Notes for the importer

1. **Merge, never replace.** The payload is a subset of a character record.
   Anything absent must survive untouched — particularly `routines` and their
   `lastDone` timestamps, `notes`, and `jobQuestsDone`. **Absent means "no
   data", never "zero".**

2. **Do not match on name.** The ledger holds *"Haurchefaunt Greystone"*; the
   game reports *"Haurche Greystone"*. Use an explicit user-chosen link, or
   `name + world` with a confirmation step. Never auto-create a record.

3. **`msqBreakdown` is completion only.** In the sample it happens to equal the
   totals because this character has finished all MSQ — do not assume that
   holds. Dawntrail reads `125` against the true `139`; the missing 14 are the
   7.4 quests the plugin does not yet carry.

4. **`quests` / `questTotals` are absent and will stay absent** until the data
   layer reports them correctly. The current build is missing Beast Tribe,
   Feature and Levequest data and runs roughly 28% short. Gate on `version`
   rather than probing for the fields.

5. **Two timestamps, different meanings.** `exported` is when the snapshot was
   taken — everything except playtime is live as of then. `playtime.asOf` is
   when lifetime playtime was last refreshed, which only happens when the
   player runs `/playtime`. If `asOf` is missing but `days`/`hours` are
   present, the figure predates this version and its age is unknown.

6. **Job names are the English sheet names, title-cased**, matching the keys
   already used in `combat` / `craft` / `gather`.

7. Raw `exp` / `expToNext` integers can be added alongside the encoded decimals
   if the ledger ever wants exact progress bars.
