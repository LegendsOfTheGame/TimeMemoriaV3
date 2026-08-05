# Reference data

Facts the game's own files do not carry. Everything here is shipped beside the
assembly and read at runtime, except where a section says otherwise.

## `toc.json` — 561 entries — **read by `TocService`**

Per-patch Main Scenario gates: the `Start` and `Final` quest ids for each patch,
plus `{Chain}Start` / `{Chain}Final` entries for the Chronicles of a New Era
raid series.

```json
{ "Patch": "5.3", "Expansion": "Shadowbringers", "Role": "Start", "Name": "...", "Ids": [69190] }
```

Needed for progression gating — deciding whether a player has reached a given
patch, so unreached content can be hidden unless spoiler mode is on. It also
drives `msqPatch` in the ledger export, which reports story position as a patch
number. The game's own tables express journal structure but not patch
boundaries, so there is no generated equivalent. This was worked out by hand and
is the reason the old repository must not simply be deleted.

### Keeping it current

Every patch from 2.0 to 7.5 has both a `Start` and a `Final` Main Scenario
quest. **When a patch adds Main Scenario, add its bookends here or story
position silently reports the previous patch.**

To find them: the Dawntrail MSQ lives in `JournalGenre` 14 (through 7.3) and 15
(7.4 onward), ordered by `sort`. The wiki names each journal section after its
closing quest, which is a useful cross-check — "Post-Dawntrail Main Scenario
Quests II" has 14 quests and ends on *Into the Mist*, matching genre 15 exactly.

Patches use the collapsed `x.y` form, so an `x.yz` patch extends the `x.y` row
rather than gaining one of its own. **7.56 is expected to continue *Trail to the
Heavens* — when it ships, move 7.5's `Final` to that arc's last quest instead of
adding a `7.56` entry.**

## `festival-names.json` — 129 entries — **read by `FestivalService`**

Festival row id to a display name:

```json
"174": "Moonfire Faire (2026)"
```

The client reports which festivals are switched on, but every row in the game's
`Festival` sheet has a blank `Name`, so an active event arrives as a bare number.

Converted from `FestivalName.csv` in
[Critical-Impact/LuminaSupplemental](https://github.com/Critical-Impact/LuminaSupplemental),
which is crowd-sourced and **GPL-3.0** — compatible with this project's AGPL-3.0.
Placeholder rows (`None`, `Unknown`) are dropped so the service falls back to
`Festival #id` rather than displaying a lie, and a few names that had been
round-tripped through a UTF-7 encoder upstream are decoded (`Yo+AC0-kai` →
`Yo-kai`).

Refresh by re-running `tools/Convert-FestivalNames.py` against a fresh copy of
the upstream CSV.

## `quest-patches.json` — 5,326 ids

Quest id to the patch that introduced it:

```json
"65821": 2.0
```

No patch data exists in the game files — there is no `Patch` sheet, only
`ExVersion` with its six expansion rows. This was fetched from Garland Tools,
which derives it by diffing XIVAPI dumps across versions.

Quests that were replaced by a later patch keep every id, and the ids disagree:
*Way of the Gladiator* is `65821` = 2.0 and `65789` = 3.1, because 3.1 added a
second route into the same content. **Resolve a multi-id quest to the lowest
patch across its ids** — the question is when the content first existed. Do not
use lowest id; the two do not correlate.

Refresh with `tools/Fetch-QuestPatches.ps1`, which is resumable.

## `quest_category_index.json` — 3,817 entries

Maps quest id to a category path and a display order:

```json
"65621": { "Path": "A Realm Reborn/Main Scenario", "Order": 12 }
```

Used by the old codebase to order class and job quests. The current data layer
gets ordering from `Quest.SortKey` and structure from `JournalGenre`, so this is
most likely redundant. Retained until that is confirmed rather than assumed.

---

All of these are read-only. If one becomes genuinely unused, delete it in a
commit that says so, rather than letting it rot here.
