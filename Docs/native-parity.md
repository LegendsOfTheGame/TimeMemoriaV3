# Native parity with Classic

Noticed 18 August 2026, by opening both windows side by side. The Native
Overview is missing things the Classic one has had for a long time, and nobody
had compared them directly since Native was built.

Not a straight port in either direction — Native is *better* in places, so the
target is the union rather than a copy.

## Closed in pass 1 (3.2.2.15, 23 August 2026)

**Story Remaining.** ✅ Now in Native, under Pacing and above Active Events:

```
129 Main Scenario quests until Dawntrail opens.
  Endwalker   129 left   ~2.5 days
  Dawntrail   139 left   ~2.7 days
268 remaining at your rate of 28m 16s per quest — roughly 5.3 days of play.
```

The arithmetic was inline in `MainWindow.DrawStoryEstimates`, which is *why*
Native went without it — the figures were not reachable from anywhere else. It
now lives in `src/Windows/StoryEstimate.cs` as a pure static helper that both
windows render, so the two cannot drift again. Its `Line` record keeps the three
columns separate precisely so Classic's layout did not have to change.

**Pacing sample size.** ✅ Native prints `across N completed quests` under
Overall, or the `/playtime` prompt when there is no lifetime figure. Native's own
pace formatter was deleted in favour of `PacingService.Format`: Story Remaining
quotes the Main Scenario rate three rows below the row showing the same number,
and two formatters made one figure look like two.

**`NewsPanelNode` now scrolls.** The 26-row ceiling is gone — it is 48 rows in a
`ScrollingNode<VerticalListNode>`. The old pool dropped surplus rows silently,
which is how nobody noticed; exhausting it now logs once. Note the panel gets a
fixed rectangle from `MainAddon` and nothing clips, so at the 460px minimum
window height surplus rows *drew over the window chrome* rather than vanishing.
Adding rows here is now safe, but hide a row's **container**, not just its two
labels — `FitContents` measures visible children, and that is the `/tmmini` bug.

## Still missing from Native

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

The row budget for this was reserved in pass 1 — the panel scrolls and holds 48
rows — so what is left here is the feed/client reconciliation (which lives
nowhere but inside `MainWindow`'s draw code) and a clickable link node. Native
has no link-with-tooltip node yet; `ProgressionPanelNode`'s Ledger button is the
closest thing, and `SelectableTextNode` is the likeliest fit for a row.

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

Pass 1 (Story Remaining, Pacing) shipped in 3.2.2.15. What that pass actually
taught: the reason Native was poorer was not neglect but *reach* — Classic's
figures were computed inside its own draw methods, where nothing else could get
at them. Whatever is drawn for the remaining two should come out of a shared
place first, or the same gap opens again the next time a window is built.
