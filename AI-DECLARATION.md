# AI usage declaration

Written ahead of submission so the disclosure in the pull request is a record of
what happened rather than something composed on the day.

Levels are those defined in Dalamud's
[AI Usage Policy](https://dalamud.dev/plugin-publishing/ai-policy/).

## Level: Copilot

**AI implements while the human plans and reviews.**

This is stated deliberately rather than reaching for a lower level. Most of the
C# in `src/` was written by Claude (Anthropic) working from direction given in
conversation. Choosing the lower-sounding "Assist" or "Pair" would misrepresent
that.

## What the human did

- Decided the plugin should be rebuilt on generated quest data rather than the
  288 hand-maintained JSON files the previous version carried, after reviewing
  the alternatives.
- Chose which existing work to build on and verified its provenance, including
  correcting a mistaken assumption about which repository was upstream.
- Tested every change in the running game. Every bug listed below was found this
  way, not by review.
- Supplied the domain knowledge the code depends on and that no tool could
  supply: that quests carrying several IDs are alternate routes rather than
  duplicates, that some are split by character gender, and how the game's
  starting-class paths diverge.
- Set the boundaries the plugin is built to (see Compliance in the README) and
  rejected suggestions that crossed them.
- Reviewed and corrected the output continuously.

## What AI got wrong

Recorded because "the AI did it" is not an acceptable answer for anything in
this repository, and because the policy asks that AI output be verified rather
than trusted:

- Disposed a native UI library before the host that owned it, crashing the game
  on plugin unload. Backed out rather than shipped, and only reinstated once the
  toolkit's actual threading contract had been read and the fix confirmed
  against two plugins by the toolkit's own author.
- Compared class levels without comparing experience within the level, so the
  lowest-levelled job was reported wrongly.
- Proposed resolving multi-ID quests by lowest ID. Wrong: *Way of the Gladiator*
  is `65821` = patch 2.0 and `65789` = patch 3.1, so the rule is lowest patch.
- Misidentified the upstream repository this work descends from.
- Misread this policy itself, claiming disclosure was required in the README.
- Read the well-fed status parameter as an item id. It is an `ItemFood` row, so
  the panel confidently reported the player was digesting a *Dated Poison
  Dagger* — Flatbread's food row is 114, and item 114 is a dagger. Caught
  because the human ate a Flatbread specifically to check the display, before it
  was committed.
- Claimed an achievement's requirement was available as a number on its sheet
  row. It is not; it exists only in the description prose. The probe written to
  prove otherwise scraped a padding column and reported a "floor of 255" for a
  tier requiring 100.
- Hypothesised that the client held a table of achievement progress, from
  misreading a screenshot the human had already supplied showing a single
  progress bar. Disproved by a probe written for the purpose, which confirmed the
  original single-slot model.
- Asserted a trial's boss from memory while a complete local copy of the wiki sat
  on disk, and had to be corrected.

The human found each of the mistakes above and corrected it before it reached
a published build.

The mistakes below reached a published build. Each one was found after the
release and fixed in a later version:

- Wrote one character's quest completions into another character's journal. A
  sweep could run between Logout and Login, when the game reported the new
  character but still held the old one's quest data. Released in builds before
  3.3.1.15; fixed in 3.3.1.15.
- Showed Allied Society quests at level 1, because the level was read without
  the sheet's level offset. Fixed in 3.4.2.15.
- Shipped a Classic "click the area to open the map" link that never worked in
  any build. Fixed in 3.4.1.15.
- Counted the Hall of the Novice exercises as duties in the export's
  `dutiesDone`, because their clear flag is not reliable. Released in 3.8.0.15;
  fixed in 3.10.0.15.
- Copied Dalamud's own libraries into the release zip, which made it 12.7 MB
  instead of 1.3 MB. Fixed in 3.11.0.15.
- Disposed the quest turn-in window's eye button in the wrong order, so every
  plugin unload failed in 3.11.0.15. Fixed in 3.11.1.15, one day later.
- Refreshed the native windows on every frame instead of once a second, which
  cost up to 1.7 ms per frame. Fixed in 3.13.0.15.

Most of these were found by running the plugin in the game and looking at what
it said. The human's testing is the load-bearing part of this arrangement, not
a formality. One was found by a code review: the per-frame refresh, which
caused no visible error.

## Assets

No asset here is AI-generated.

- `icon.png` — **drawn by hand by the maintainer in MS Paint**, after the
  Tomestone of Poetics. `icon-source-1024.png` is the original; the shipped copy
  is that file resampled to 512x512 with Pillow's Lanczos filter to meet
  Dalamud's size cap. That is arithmetic on pixels, not a generative or
  ML-upscaling step — no detail was invented that the maintainer did not draw.
- `overview.png`, `quests.png`, `progression.png`, `mini.png` — screenshots of
  this plugin running, captured by the maintainer. Unretouched. The set the
  repository shipped with previously showed the original QuestTracker and has
  been replaced.
- `assets/d17/image1.png` to `image3.png` — crops of the maintainer's
  screenshots, cut to 730x380 or smaller for the Dalamud plugin listing.
  Unretouched. `assets/d17/icon.png` is the same hand-drawn icon at 512x512,
  prepared for the listing.

If any of this changes, this section changes with it — the policy asks for asset
disclosure to be more visible than code disclosure, since assets are what users
actually see.

## Derived code

Portions of `src/` descend from [QuestTracker](https://github.com/keifufu/QuestTracker)
(isaiahcat, keifufu), used under AGPL-3.0 and predating any AI involvement here.
See [NOTICE](NOTICE).

## Standard the maintainer holds to

The maintainer can explain any file in this repository, has tested the plugin
personally, and treats AI-written code as their own work for the purposes of
review and blame.
