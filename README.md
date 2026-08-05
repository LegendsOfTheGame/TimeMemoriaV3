# Time Memoria

A FFXIV Dalamud plugin for quest progression and pacing — a reflective notebook
for your story journey, not a scoreboard.

> ⚠️ This plugin **cannot** be used for ACT/FFLogs-style performance analysis.
> It does not track, read, or evaluate combat performance in any way.

---

## Status: unreleased

**Time Memoria has never been released.** Nothing has been published and nobody
has installed it. When this repository ships, it will be version 1.0.

An earlier attempt exists at
[TimeMemoriaV2](https://github.com/LegendsOfTheGame/TimeMemoriaV2). It is kept
for reference, not as a released version. It maintained its quest data by hand —
288 JSON files needing an update every patch — and that is why it stalled:
patch 7.5 landed in April 2026 and the data was never added.

This rebuild reads quest data from the game's own journal tables instead, so a
patch's content appears the day it lands and no one has to do anything.

### What works

- Quest data generated from the game's own journal tables — no hand-maintained
  quest files, so a patch's content appears the day it lands
- Two-panel questline tree, chain-grouped, with search across every expansion
  including ones the tree is hiding
- Suggested next quest, and per-expansion and per-category progress
- MSQ progression gating, spoiler mode, free-trial mode
- Class and job levelling, role-coloured, with clipboard export
- Playtime pacing, session and lifetime
- World state: maintenance windows, patch status, and active events read from
  the client rather than only from the news feed
- What's New — quests added since the baseline snapshot, with the build they
  arrived in
- Per-character completion dates from first install onwards
- Help and Credits

### Not finished

- **Patch attribution.** `data/quest-patches.json` maps 5,326 quest ids to the
  patch that introduced them, and nothing reads it yet.
- **Native UI.** One tab — Progression — exists as a real game window and
  unloads cleanly. The rest is still ImGui.
- **Screenshots.** Those in `assets/` are the original QuestTracker's and must
  be retaken.

---

## Building

Native UI comes from [KamiToolKit](https://github.com/MidoriKami/KamiToolKit),
which is not published as a package — it is a submodule, and **the build will
fail without it**:

```
git clone --recurse-submodules https://github.com/LegendsOfTheGame/TimeMemoriaV3
```

Already cloned? `git submodule update --init --recursive`, then:

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

## AI usage

Development was AI-assisted at the **Copilot** level as defined by Dalamud's
[AI Usage Policy](https://dalamud.dev/plugin-publishing/ai-policy/) — AI
implements, the maintainer plans, tests and reviews. No assets are AI-generated.

[AI-DECLARATION.md](AI-DECLARATION.md) records this in full, including what AI
got wrong and how it was caught.

---

## Licence

[AGPL-3.0](LICENSE).
