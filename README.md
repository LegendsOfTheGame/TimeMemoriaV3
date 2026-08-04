# Time Memoria

A FFXIV Dalamud plugin for quest progression and pacing — a reflective notebook
for your story journey, not a scoreboard.

> ⚠️ This plugin **cannot** be used for ACT/FFLogs-style performance analysis.
> It does not track, read, or evaluate combat performance in any way.

---

## Status: pre-release rebuild

This repository is a **ground-up rebuild** and is not yet the shipping plugin.
The released Time Memoria lives at
[TimeMemoriaV2](https://github.com/LegendsOfTheGame/TimeMemoriaV2).

It exists because the previous version maintained its quest data by hand — 288
JSON files that had to be updated every patch. That did not survive contact with
a release schedule: patch 7.5 shipped in April 2026 and the data was never
added. This rebuild reads quest data from the game's own tables instead, so new
content appears the day a patch lands and no one has to do anything.

Versioned from `0.1.0.0` and deliberately off the shipping version line until it
reaches parity.

### What works

- Quest browsing, generated from game data — no hand-maintained quest files

### What is coming across from the shipping plugin

- Two-panel questline tree with chain-grouped sections
- Suggested next quest
- Playtime pacing, session and lifetime
- World state: maintenance windows, patch status, seasonal events
- MSQ progression gating, spoiler mode, free-trial mode
- Class and job levelling with clipboard export

---

## Building

```
dotnet build TimeMemoria.csproj
```

Requires the .NET 10 SDK and a Dalamud development environment. The assembly is
named `TimeMemoriaV3` so it can be loaded alongside the released plugin for
comparison.

---

## Credits

Time Memoria's quest data layer derives from **QuestTracker**, originally by
[isaiahcat](https://github.com/isaiahcat/QuestTracker) and currently maintained
by [keifufu](https://github.com/keifufu/QuestTracker), used under AGPL-3.0. It
is an independent plugin, not affiliated with or endorsed by either author.

See [NOTICE](NOTICE) for the full attribution and the list of changes.

Inspiration, though no code, also came from
[BetterPlaytime](https://github.com/caitlyn-gg/BetterPlaytime) by Infi and
Caitlyn, and [LeveHelper](https://github.com/Haselnussbomber/LeveHelper) by
Haselnussbomber.

---

## Compliance

Time Memoria is strictly a quest progression and pacing tool.

It does **not**:

- Read or display DPS, HPS, deaths, wipes, or duty results
- Integrate with ACT, FFLogs, or any combat log format
- Automate any in-game interaction
- Transmit your data anywhere — the clipboard export is local and user-initiated

Saved data is limited to quest IDs, completion counts, timestamps, pacing
aggregates, and class/job levels. Nothing it stores can be repurposed as a
combat log.

---

## Licence

[AGPL-3.0](LICENSE).
