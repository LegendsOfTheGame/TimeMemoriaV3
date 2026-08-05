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

Each was caught by the human and corrected before it reached anyone.

## Assets

No asset here is AI-generated.

- `icon.png` — **drawn by hand by the maintainer in MS Paint**, after the
  Tomestone of Poetics. `icon-source-1024.png` is the original; the shipped copy
  is that file resampled to 512x512 with Pillow's Lanczos filter to meet
  Dalamud's size cap. That is arithmetic on pixels, not a generative or
  ML-upscaling step — no detail was invented that the maintainer did not draw.
- `overview.png`, `quests.png`, `settings.png` — **stale, and not this plugin.**
  They are screenshots of the original QuestTracker from when the game held
  around 4,360 quests, showing three tabs where this plugin has eight. They must
  be retaken before submission.

If any of this changes, this section changes with it — the policy asks for asset
disclosure to be more visible than code disclosure, since assets are what users
actually see.

## Derived code

Portions of `src/` descend from [QuestTracker](https://github.com/keifufu/QuestTracker)
(isaiahcat, keifufu), used under AGPL-3.0 and predating any AI involvement here.
See [NOTICE](NOTICE).

## For the submission pull request

The policy asks for the disclosure in the PR description. This paragraph carries
it:

> **AI disclosure:** Copilot level. AI wrote most of the C# under my direction; I
> planned the architecture, tested every change in game, and reviewed and
> corrected the output. No assets are AI-generated. Full declaration, including
> the mistakes AI made and how they were caught, is in
> [AI-DECLARATION.md](AI-DECLARATION.md) in the plugin repository.

Update the asset sentence if that stops being true before submission.

## Standard the maintainer holds to

The maintainer can explain any file in this repository, has tested the plugin
personally, and treats AI-written code as their own work for the purposes of
review and blame.
