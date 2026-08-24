"""Generate repo.json, the plugin repository manifest Dalamud reads.

Dalamud third-party repositories are a JSON array of plugin manifests, each
carrying download links. Aggregators — such as WilliamW1979/FFXIV — fetch this
file and merge it with others, so it has to be reachable at a stable public URL
and its AssemblyVersion has to match the release it points at.

Everything except the links is copied from the manifest DalamudPackager already
writes, so the two cannot drift apart.

Usage:
    python tools/Build-RepoJson.py
    python tools/Build-RepoJson.py "Fixed session pacing on character switch."
"""

import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
BUILT = ROOT / 'bin' / 'Release' / 'TimeMemoriaV3' / 'TimeMemoriaV3.json'
CSPROJ = ROOT / 'TimeMemoria.csproj'
OUT = ROOT / 'repo.json'

OWNER = 'LegendsOfTheGame'
REPO = 'TimeMemoriaV3'

# "releases/latest" resolves to whatever the newest release is, so these links
# survive every future version without being rewritten.
DOWNLOAD = f'https://github.com/{OWNER}/{REPO}/releases/latest/download/latest.zip'
ICON = f'https://raw.githubusercontent.com/{OWNER}/{REPO}/main/assets/icon.png'

# Dalamud's installer groups by these. Lowercase: that is what its own enum uses,
# and what most published plugins actually write, though the field is loose
# enough that both cases appear in the wild.
CATEGORY_TAGS = ['utility']


def intended_version():
    """The version this working tree means to release, from the csproj.

    The single source of truth for a release. The built manifest is downstream of
    it and can lag; this cannot.
    """
    text = CSPROJ.read_text(encoding='utf-8-sig')
    match = re.search(r'<Version>\s*([0-9]+(?:\.[0-9]+)*)\s*</Version>', text)
    if not match:
        raise SystemExit(f'no <Version> found in {CSPROJ}')
    return match.group(1)


def check_version(manifest):
    """Refuse to write a manifest advertising a version nobody meant to ship.

    An ordinary `dotnet build` leaves bin/Release/.../TimeMemoriaV3.json untouched
    when only <Version> changed, so this script would read the *previous* version
    and write a repo.json advertising it. Nothing errors anywhere: the tag exists,
    the release exists, the download link resolves to it — and every installer
    compares AssemblyVersion, sees no increase, and reports no update. That shipped
    as 3.0.3.15.

    Docs/releasing.md answers this with `--no-incremental` and an instruction to
    eyeball the printed version against the tag. This is the same check, made
    mandatory: correctness that depends on a person remembering to look is
    correctness that lapses the first time someone is in a hurry.
    """
    intended = intended_version()
    built = str(manifest.get('AssemblyVersion', ''))
    if built == intended:
        return

    raise SystemExit(
        f'refusing to write {OUT.name}: stale build output.\n'
        f'  csproj <Version>          {intended}\n'
        f'  built manifest            {built}\n'
        '\n'
        'The build predates the version bump, so this manifest would advertise a\n'
        'version installers already have and the release would be invisible.\n'
        'Rebuild and run again:\n'
        '  dotnet build -c Release --no-incremental'
    )


def main(changelog=None):
    if not BUILT.exists():
        raise SystemExit(f'{BUILT} not found — run: dotnet build -c Release')

    manifest = json.loads(BUILT.read_text(encoding='utf-8-sig'))
    check_version(manifest)

    # DownloadLinkTesting deliberately points at the stable zip: there is no
    # testing channel here, because DalamudPluginsD17 supplies one as a directory
    # (testing/live -> stable) and that is where the plugin is headed. Dalamud
    # only follows this link when TestingAssemblyVersion exists and exceeds
    # AssemblyVersion, so it is inert — but adding that key without also pointing
    # this at a prerelease artifact would serve testers the stable build under the
    # test version's name. See Docs/releasing.md.
    manifest.update({
        'CategoryTags': CATEGORY_TAGS,
        'IsHide': False,
        'IsTestingExclusive': False,
        'DownloadLinkInstall': DOWNLOAD,
        'DownloadLinkUpdate': DOWNLOAD,
        'DownloadLinkTesting': DOWNLOAD,
        'IconUrl': ICON,
        'DownloadCount': 0,
    })

    # Shown in the installer beside the update. Omitted rather than left empty,
    # since an empty changelog reads as "nothing changed".
    if changelog:
        manifest['Changelog'] = changelog

    OUT.write_text(json.dumps([manifest], indent=2) + '\n', encoding='utf-8')

    print(f'wrote {OUT}')
    print(f'  {manifest["Name"]} {manifest["AssemblyVersion"]}  API {manifest["DalamudApiLevel"]}')
    print()
    print('Submit this URL:')
    print(f'  https://raw.githubusercontent.com/{OWNER}/{REPO}/main/repo.json')


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else None)
