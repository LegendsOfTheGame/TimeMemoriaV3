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
  }
}
```

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
| `msqBreakdownTotals` | — | **Deliberately not sent.** Static reference data the ledger already holds, and ours understates Dawntrail |
| `quests` / `questTotals` | — | **Not sent.** See caveat |

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
