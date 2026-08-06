"""Generate repo.json, the plugin repository manifest Dalamud reads.

Dalamud third-party repositories are a JSON array of plugin manifests, each
carrying download links. Aggregators — such as WilliamW1979/FFXIV — fetch this
file and merge it with others, so it has to be reachable at a stable public URL
and its AssemblyVersion has to match the release it points at.

Everything except the links is copied from the manifest DalamudPackager already
writes, so the two cannot drift apart.

Usage:
    python tools/Build-RepoJson.py
"""

import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
BUILT = ROOT / 'bin' / 'Release' / 'TimeMemoriaV3' / 'TimeMemoriaV3.json'
OUT = ROOT / 'repo.json'

OWNER = 'LegendsOfTheGame'
REPO = 'TimeMemoriaV3'

# "releases/latest" resolves to whatever the newest release is, so these links
# survive every future version without being rewritten.
DOWNLOAD = f'https://github.com/{OWNER}/{REPO}/releases/latest/download/latest.zip'
ICON = f'https://raw.githubusercontent.com/{OWNER}/{REPO}/main/assets/icon.png'


def main():
    if not BUILT.exists():
        raise SystemExit(f'{BUILT} not found — run: dotnet build -c Release')

    manifest = json.loads(BUILT.read_text(encoding='utf-8-sig'))

    manifest.update({
        'IsHide': False,
        'IsTestingExclusive': False,
        'DownloadLinkInstall': DOWNLOAD,
        'DownloadLinkUpdate': DOWNLOAD,
        'DownloadLinkTesting': DOWNLOAD,
        'IconUrl': ICON,
        'DownloadCount': 0,
    })

    OUT.write_text(json.dumps([manifest], indent=2) + '\n', encoding='utf-8')

    print(f'wrote {OUT}')
    print(f'  {manifest["Name"]} {manifest["AssemblyVersion"]}  API {manifest["DalamudApiLevel"]}')
    print()
    print('Submit this URL:')
    print(f'  https://raw.githubusercontent.com/{OWNER}/{REPO}/main/repo.json')


if __name__ == '__main__':
    main()
