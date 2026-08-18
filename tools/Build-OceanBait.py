"""
Builds OceanBait.json from Distant Seas' fishing data.

Run this occasionally — monthly is plenty — and publish the result. It is not
run by the plugin, and the plugin never contacts NotNite's repository; it reads
whatever file this produced.

Only two things are taken from each zone: the fish that can trigger a spectral
current, and the bait that catches it. Bite times are deliberately left out, as
they move on hotfixes and would be presented as current while being stale.

Item names come from the local wiki corpus so the output is human-readable
without the plugin needing a lookup; ids are kept as the authority.
"""

import json
import subprocess
import sqlite3
import datetime

SOURCE = "https://github.com/NotNite/DistantSeas"
DB = "E:/wiki/ffxiv_wiki.db"
ROUTES = ("indigo", "ruby")

# Zones where the trigger fish was actually landed on the listed bait in game.
# A landed fish is the only clean evidence: a spectral current is party-wide and
# probabilistic, so on a two-person boat its absence says nothing at all.
VERIFIED = {
    "SirensongSea": "2026-08-18",
    "KuganeCoast": "2026-08-18",
    "RubySea": "2026-08-18",
    "CieldalaesMargin": "2026-08-18",
}


def item_names():
    """id -> name, from the local wiki corpus."""
    names = {}
    con = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
    query = ("select title, infobox_json from pages "
             "where template_name like '%Item infobox%' and infobox_json is not null")
    for title, blob in con.execute(query):
        try:
            data = json.loads(blob)
        except json.JSONDecodeError:
            continue
        gid = data.get("id-gt")
        if gid and str(gid).strip().isdigit():
            names[int(str(gid).strip())] = title
    con.close()
    return names


def fetch(route):
    url = subprocess.run(
        ["gh", "api", f"repos/NotNite/DistantSeas/contents/Data/{route}.json", "--jq", ".download_url"],
        capture_output=True, text=True, check=True).stdout.strip()
    return json.loads(subprocess.run(["curl", "-s", url], capture_output=True, text=True, check=True).stdout)


def build():
    names = item_names()
    out = {
        "source": SOURCE,
        "licence": "AGPL-3.0",
        "built": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "note": "Spectral-current trigger fish and their bait. Bite times deliberately omitted.",
        "routes": {},
    }

    for route in ROUTES:
        zones = {}
        for zone in fetch(route):
            if zone["IsSpectral"]:
                continue

            triggers = [f for f in zone["Fish"] if f.get("CanCauseSpectral")]
            if not triggers:
                continue

            # One per zone in every case observed; if that ever stops being true
            # the extra entries should be visible rather than silently dropped.
            if len(triggers) > 1:
                print(f"  note: {zone['Type']} has {len(triggers)} trigger fish")

            fish = triggers[0]
            baits = sorted(int(b) for b, info in (fish.get("BiteTimes") or {}).items()
                           if info.get("CellType") == "BestOrRequired")

            if not baits:
                print(f"  note: {zone['Type']} trigger fish has no flagged bait")
                continue

            # Two zones flag two baits each, and shipping both would be shipping
            # the ambiguity rather than an answer. Bait ids ascend with tier --
            # Ragworm 29714, Krill 29715, Plump Worm 29716 -- so the lowest id is
            # the cheapest bait that works.
            #
            # Verified at Kugane Coast on 18/08/2026: flagged [Ragworm, Krill],
            # and a spectral wrasse landed on Ragworm after eight casts while
            # five casts of Krill produced none. Distant Seas lists both, one
            # community site picks the lower and was right, another picks the
            # higher and was wrong. One data point, so the discarded bait is kept
            # visible rather than dropped.
            chosen = baits[0]

            entry = {
                "fish": names.get(fish["ItemId"], str(fish["ItemId"])),
                "fishId": fish["ItemId"],
                "bait": names.get(chosen, str(chosen)),
                "baitId": chosen,
            }

            if len(baits) > 1:
                entry["alsoFlagged"] = [names.get(b, str(b)) for b in baits[1:]]

            if zone["Type"] in VERIFIED:
                entry["verifiedInGame"] = VERIFIED[zone["Type"]]

            zones[zone["Type"]] = entry

        out["routes"][route] = zones
        print(f"{route}: {len(zones)} zones")

    return out


if __name__ == "__main__":
    result = build()
    path = "OceanBait.json"
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(result, handle, indent=2, ensure_ascii=False)
        handle.write("\n")
    print(f"\nwrote {path}")
    print(json.dumps(result, indent=2, ensure_ascii=False)[:900])
