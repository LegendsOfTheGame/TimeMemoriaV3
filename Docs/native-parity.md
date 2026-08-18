# Native parity with Classic

Noticed 18 August 2026, by opening both windows side by side. The Native
Overview is missing things the Classic one has had for a long time, and nobody
had compared them directly since Native was built.

Not a straight port in either direction — Native is *better* in places, so the
target is the union rather than a copy.

## Missing from Native entirely

**Story Remaining.** The whole section, and arguably the plugin's headline
feature:

```
129 Main Scenario quests until Dawntrail opens.
  Endwalker   129 left   ~2.5 days
  Dawntrail   139 left   ~2.7 days
268 remaining at your rate of 28m 16s per quest — roughly 5.3 days of play.
```

**Maintenance.** Upcoming, and the last one with its end date. Native shows
nothing at all, so a player on Native cannot tell whether the feed is quiet or
the section is broken.

## Present but degraded

**Active Events.** Native prints `running`. Classic prints `Ends in 7d 18h`, a
`[read]` link to the Lodestone article, and — for an event the feed does not
carry — `Running now — end date not published to the feed`, followed by
`1 event is live in game but absent from the news feed.`

That last line is not decoration. It is the window explaining a discrepancy it
detected between the game and the feed, and Native drops it silently, which is
the worst of the three options: no end date, and no reason given.

**Pacing.** Classic grounds the figure with `across 1345 completed quests`.
Without it, "15m per quest" has no sample size attached.

## Better in Native, keep

- `Counting since 16:49` — says when the session clock started, which Classic
  does not.
- Main Scenario pacing as its own row, rather than buried in Story Remaining.

## Why this matters more than it looks

Native is the window being developed; Classic is the one being kept working. So
the gap runs the wrong way — the newer window is the poorer one, and a player who
switches to Native loses the projection that most justifies the plugin.

Nothing here needs new data. Every figure above is already computed and already
rendered by Classic, so this is a rendering gap, not a capability one.
