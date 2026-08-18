# Gear and Food

Two features that fell out of one question — can the plugin read your inventory —
established by probe on 18 August 2026. Neither is built yet. This records what
the client actually exposes, so the design does not have to be re-derived.

Both are read-only, local, and user-opened. Nothing is consumed, equipped or
purchased on the player's behalf.

## Why these belong in a quest tracker

Food is a quest reward. The main scenario hands it out every few quests, the
plugin already loads the `Quest` sheet, and it is arguably the thing that put the
food in your bags. The gear advisor's case is weaker on that argument and rests
on a different one: it needs your **crafter levels**, which the plugin already
reads for the Progression tab, and which no website can know.

## The reads that were confirmed

| Question | Answer |
|---|---|
| Equipped gear | `InventoryManager`, `InventoryType.EquippedItems`, 14 slots |
| Bags and saddlebag | Same call, `Inventory1`–`Inventory4`, `SaddleBag1`–`2` |
| Item stats | `Item.BaseParam` / `BaseParamValue`, six pairs |
| HQ stats | `BaseParamSpecial` / `BaseParamValueSpecial` |
| Melded materia | `InventoryItem.Materia` / `MateriaGrades`, five each |
| Food effect | `Item.ItemAction` → `Data[1]` → `ItemFood` |
| Well-fed status | `ItemAction.Data[0]` is **48** on every food |
| Recipes | `Recipe`, indexed by `ItemResult` |
| Shop items | `SpecialShop`, `Item[].ReceiveItems`, costs in `ItemCosts` |

Nothing needed `RequestAchievementProgress` or any network call.

## Equipped slot layout

Size is 14. Container index maps to `EquipSlotCategory − 1` for indices 0–12,
both rings share category 12, and the soul crystal breaks the pattern at index 13
(category 17).

**Index 5 is the waist slot** — still present, permanently empty since it stopped
being used in Endwalker. It has to be skipped rather than treated as a gap.

`EquipSlotCategory` has named columns, so left and right side need no magic
numbers: right side is Ears, Neck, Wrists, FingerL/R. That matters because class
quests hand out left-side gear the whole way up and **never once give an
accessory** — the right side has no passive source at any level, and the first
scrip accessory is level 58. So for levels 1–57 the right side has essentially one
source, and that is where advice is worth most.

## Gear stats reconcile, but only partly

Measured on a level 61 Fisher in a full IL 200 set:

```
             gear   panel   base
Vitality      132     355    223
Gathering     702     702      0
Perception    575     575      0
GP            257     657    400
```

Gathering and Perception come entirely from equipment. Vitality and GP have a base
from level and class that no gear read will produce.

**So the two features need different reads.** Gear comparison works on *deltas*
and needs only sheet data. Food works on *totals* and needs live attributes,
because a food percentage applies to the whole. They are not the same foundation.

What they *do* share is the table of which stats a class cares about — that is
load-bearing in both.

## Item level is the wrong ranking

The finding that justifies the whole feature.

`Augmented Shire Custodian's Earrings` are item level 270, categorised **All
Classes**, and equippable by a Fisher. They grant Strength 47, Vitality 49,
Critical Hit 32, Direct Hit Rate 46. Against `Augmented Landmaster's Earrings` at
item level 200 — GP 60, Gathering 5 — swapping costs 60 GP and returns nothing.

Item level is a stat *budget*, not stat *quality*. The rule "higher item level
wins" holds **within** a role and breaks at role boundaries, which is exactly
where All Classes items live. And for gatherers and crafters it is guaranteed to
break at the top, because at level 60 there is no item level 270 DoL gear at all —
an item-level sort inevitably reaches past the end of the relevant range.

**The filter: a candidate must grant at least one stat this job uses.** That one
rule also removes the il1 cosmetics (Chocobo Suit, Snowman Suit, Reindeer Suit)
and legacy All Classes armour, with no blocklist and no special cases.

## ClassJobCategory is a value signal, not just a filter

```
il200 eq60  Augmented Tacklekeep's Vest   [FSH]
il200 eq63  Gyuki Leather Jacket          [Disciple of the Land]
```

Same item level. The first is locked to Fisher; the second serves all three
gatherers. The wiki's own guide recommends skipping the scrip armour for exactly
this reason, and the distinction is already in the data.

**But that guide is wrong for a player whose Leatherworker is 57.** It assumes
crafters at 63. That gap — universal advice versus advice conditioned on your
actual classes — is the entire thesis of the feature.

## Never point at the cash shop

Mog Station items appear as candidates: `Zero's Order Cuirass` is real money and
shows as "no recipe, no shop". The stat filter removes it incidentally, but the
rule should be explicit rather than lucky:

> no recipe, no shop, no stats → not obtainable in game → never surface it

That also covers expired event rewards and anything else the player cannot act on.
Silence is the correct output. The wiki corpus carries `source-type: Premium`,
which confirmed the assumption behind the negative test — Square does not sell
stat gear — without anything needing to ship.

## Food

### Everything is cap-bound

Food grants a percentage with a hard cap: `min(stat × pct, cap)`. Measured
against a Fisher at Gathering 702, Perception 575, GP 657, **every food in the
bags was limited by its cap, not its percentage.** Blood Currant Tart's Gathering
cap binds above 171.

So **rank by cap, not by computed bonus.** The percentage only matters below a few
hundred in a stat, which is a low-level character. That is a much smaller feature
than a live calculation.

### Two tiers, and only the first is needed to ship

- **Tier 1** — do you have food, and are you fed. Every meal carries the same
  experience bonus regardless of whether its stats apply, so crafting food on a
  combat job is a correct answer. No stats, no arithmetic.
- **Tier 2** — which of the pile is best. Cap sort, HQ preferred, class-filtered.

Tier 1 works for anyone. On the character this was measured on: **32 food stacks
across 130 occupied slots**, and the player did not know they owned ten Flatbread.
The feature's real job is not ranking, it is telling you what is in your own bags.

### The sort direction flips

When nothing in the bags helps the current class, the goal inverts — you want the
experience bonus, so **minimise what you burn**: lowest item level, NQ before HQ,
largest stack. Recommending someone eat their only HQ meal for an experience tick
is worse advice than saying nothing.

### Class relevance

`ClassJob.Role` and the DoH/DoL categories give the split from sheet data rather
than a hardcoded table, consistent with jobs already being read from sheets. Most
of it is also inferable from what your own gear grants — Tenacity appears only on
tank gear, Piety only on healer gear, Gathering only on gatherer gear. One
exception needs handling: **Vitality is on everything but only useful in combat.**

Recompute on class change. The same bag gives completely different answers for a
Fisher and a Weaver.

## Performance is a non-issue

```
Recipe index      11,341 items                 1–3ms
SpecialShop       12,886 items / 1,489 shops  14–30ms
Item sweep        52,801 rows                 18–36ms
```

Under 70ms cold. It runs on demand when the panel opens; no startup cost, no
caching, no background work.

## Still unprobed

1. **Live attribute totals** — needed for food's tier 2, since gear sums miss the
   base. Not located.
2. **The two shop gates.** `SpecialShop.Item` carries `Quest` and
   `AchievementUnlock`. "Offered by a special shop" is not the same as "you can
   buy this", and recommending gear from a vendor you cannot talk to is exactly
   the confidently-wrong advice that kills trust in a feature.
3. **Prices.** `ItemCosts` holds currency and amount. The currency matters more
   than the number — "offered by a special shop" is useless across 1,489 shops,
   while "costs purple gatherers' scrips" is directions. The amount is free once
   the field is read, and enables the one line worth having: *you already have
   enough.*
4. **Mentor counts.** Commendations and duty totals are running counts, not
   achievements. Nothing has confirmed they are readable.
5. **Source for items with neither recipe nor shop** — relic steps, quest rewards,
   drops. Currently indistinguishable from unobtainable. The wiki corpus is the
   only source that has it, but the field is not uniform across infobox templates,
   so it is not the tidy join it first appeared to be.

## Empty states are the default case

For a quest tracker the audience is mid-progression, so "user has none of this" is
the normal case, not an edge case: no scrips, no levelled crafters, ocean fishing
not unlocked, no interest in mentor status. Every panel needs a defined behaviour
for it, and must not nag someone who does not want the feature.

Food is the exception that proves the rule — the game gives out so much of it that
almost nobody has an empty bag, which is why it is the first of these to build.
