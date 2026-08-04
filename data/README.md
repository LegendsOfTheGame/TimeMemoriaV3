# Preserved data

Hand-derived data carried over from the previous codebase. **Not currently read
by the plugin** — kept here because it cannot be regenerated from game files and
would otherwise be lost when the old repository is archived.

## `toc.json` — 561 entries

Per-patch Main Scenario gates: the `Start` and `Final` quest ids for each patch,
plus `{Chain}Start` / `{Chain}Final` entries for the Chronicles of a New Era
raid series.

```json
{ "Patch": "5.3", "Expansion": "Shadowbringers", "Role": "Start", "Name": "...", "Ids": [69190] }
```

Needed for progression gating — deciding whether a player has reached a given
patch, so unreached content can be hidden unless spoiler mode is on. The game's
own tables express journal structure but not patch boundaries, so there is no
generated equivalent. This was worked out by hand and is the reason the old
repository must not simply be deleted.

## `quest_category_index.json` — 3,817 entries

Maps quest id to a category path and a display order:

```json
"65621": { "Path": "A Realm Reborn/Main Scenario", "Order": 12 }
```

Used by the old codebase to order class and job quests. The current data layer
gets ordering from `Quest.SortKey` and structure from `JournalGenre`, so this is
most likely redundant. Retained until that is confirmed rather than assumed.

---

Both files are read-only reference data. If either becomes genuinely unused,
delete it in a commit that says so, rather than letting it rot here.
