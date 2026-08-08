# Releasing

Order matters: the repository manifest carries a version and a download link, so
it can only be written once the release it points at exists.

## Once, before the first release

1. **Make the repository public.** Required twice over — AGPL-3.0 obligates
   source availability to anyone the plugin is distributed to, and an aggregator
   fetching `repo.json` over HTTP simply cannot read a private repository.
2. Retake the screenshots in `assets/`. They are still the original
   QuestTracker's.

## Every release

Now that the plugin is published, **any fix that reaches users is a release**.
There is no such thing as "just a commit" for a behaviour change: whoever
installed it has the previous build, and only a higher version tells Dalamud to
replace it. Bump before committing, not after.

1. Set `<Version>` in `TimeMemoria.csproj`. Format is `MAJOR.MINOR.PATCH.API` —
   the last digit is the Dalamud API level, so 3.0.0 on API 15 is `3.0.0.15`.
2. Commit and push.
3. Tag it and push the tag:

   ```
   git tag v3.0.0.15
   git push origin v3.0.0.15
   ```

   CI builds Release and attaches `latest.zip` to a GitHub release. The tag must
   start with `v` or the release step is skipped.
4. Regenerate and commit the repository manifest:

   ```
   dotnet build -c Release --no-incremental
   python tools/Build-RepoJson.py "What changed, in a sentence."
   git add repo.json && git commit -m "release: 3.0.0.15" && git push
   ```

   `repo.json` copies everything from the manifest DalamudPackager writes, so
   the version there always matches the build. Its download links point at
   `releases/latest`, which resolves to the newest release on its own and never
   needs editing.

   **`--no-incremental` is not optional.** An ordinary build leaves
   `bin/Release/TimeMemoria V3/TimeMemoriaV3.json` untouched when only the
   version changed, so `Build-RepoJson.py` happily reads the *previous*
   version and writes a manifest advertising it. Nothing errors. The release
   exists on GitHub, the download link resolves to it, and every installer
   decides there is no update — because `AssemblyVersion` is what Dalamud
   compares. This happened to 3.0.3.15; check the version the script prints
   against the tag before committing.

## Listing it somewhere

The manifest lives at:

```
https://raw.githubusercontent.com/LegendsOfTheGame/TimeMemoriaV3/main/repo.json
```

That URL is what anyone adds to Dalamud's custom repository list, and what
aggregators want. [WilliamW1979/FFXIV](https://github.com/WilliamW1979/FFXIV)
keeps a plain `RepoList.txt` of such URLs and merges them every five minutes —
submitting means getting one line added to that file, by issue or by pull
request.

## Submitting to the official repository

Separate process, and stricter: a pull request to
[goatcorp/DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17).
Disclose AI involvement in the PR description — see [AI-DECLARATION.md](../AI-DECLARATION.md)
for the wording, which is already written.
