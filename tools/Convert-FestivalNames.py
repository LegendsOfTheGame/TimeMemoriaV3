"""Convert LuminaSupplemental's FestivalName.csv into data/festival-names.json.

The game's Festival sheet ships with every Name blank, so an active event
arrives from the client as a bare row id. Critical-Impact/LuminaSupplemental
keeps a crowd-sourced list of what those ids actually are (GPL-3.0).

Usage:
    curl -sLO https://raw.githubusercontent.com/Critical-Impact/LuminaSupplemental/main/src/LuminaSupplemental.Excel/Generated/FestivalName.csv
    python tools/Convert-FestivalNames.py FestivalName.csv
"""

import csv
import json
import pathlib
import sys

DST = pathlib.Path(__file__).resolve().parent.parent / 'data' / 'festival-names.json'

# Part of the upstream csv was round-tripped through a utf-7 encoder at some
# point, so a few punctuation marks survive as their escape sequences.
UTF7 = {
    '+AC0-': '-',
    '+ACY-': '&',
    '+ACI-': '"',
    '+AF8-': '_',
}

# Placeholders upstream uses for rows nobody has identified. Dropping them lets
# the service fall back to "Festival #id", which is more honest than a label
# reading "Unknown".
SKIP = {'unknown', 'none', ''}


def main(src):
    names = {}
    dropped = []

    with open(src, encoding='utf-8', newline='') as fh:
        for row in csv.DictReader(fh):
            name = row['Name'].strip()
            for bad, good in UTF7.items():
                name = name.replace(bad, good)

            if name.lower() in SKIP:
                dropped.append(row['FestivalId'].strip())
                continue

            names[row['FestivalId'].strip()] = name

    DST.write_text(
        json.dumps(names, indent=1, sort_keys=True, ensure_ascii=False),
        encoding='utf-8')

    print(f'wrote {len(names)} names to {DST}')
    print(f'dropped {len(dropped)} placeholder rows: {dropped}')

    leftover = {k: v for k, v in names.items() if '+A' in v}
    if leftover:
        print(f'WARNING: undecoded escapes remain, add them to UTF7: {leftover}')


if __name__ == '__main__':
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
