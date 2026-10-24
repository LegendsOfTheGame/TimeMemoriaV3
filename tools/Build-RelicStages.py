"""
Builds the relic stage table: item id -> relic line, job and stage.

Time Memoria needs it to turn the weapons a character holds into stage codes
for the collections export ("RSWAR-A" = Resistance Weapons, Warrior, stage 1).
Pandora Lunar needs the same stages to name them. One script writes both, so
the two can never disagree on what letter a stage is.

Source: the local wiki corpus. Every relic item page names its stage in
"set-name" (Skullrender: "Base Resistance Weapons") and carries the game's own
item id in "id-gt" (30230, the id MemoriaProbe read on 24 Oct 2026). Replicas
have set names of their own, so they never match.

Run it when a patch adds a relic stage. Dawntrail's lines are final (7.56).

Usage:
  python tools/Build-RelicStages.py [path/to/pandora-lunar]
"""

import re
import sqlite3
import sys
from pathlib import Path

DB = "E:/wiki/ffxiv_wiki.db"
ROOT = Path(__file__).resolve().parent.parent
CS_OUT = ROOT / "src" / "Services" / "Data" / "RelicStages.g.cs"

# Line code, expansion (0 = ARR), display name, then each stage's wiki set name
# in upgrade order. A stage's letter is its place here: A is the first.
# Order checked against item levels, 24 Oct 2026.
LINES = [
    ("ZD", 0, "Zodiac Weapons", [
        "Base Zodiac Weapons", "Zenith Zodiac Weapons", "Atma Zodiac Weapons",
        "Animus Zodiac Weapons", "Novus Zodiac Weapons", "Nexus Zodiac Weapons",
        "Zodiac Braves Weapons", "Zodiac Zeta Weapons"]),
    ("AN", 1, "Anima Weapons", [
        "Animated Weapons", "Awoken Weapons", "Base Anima Weapons",
        "Hyperconductive Anima Weapons", "Reconditioned Anima Weapons",
        "Sharpened Anima Weapons", "Complete Anima Weapons", "Lux Anima Weapons"]),
    ("EU", 2, "Eurekan Weapons", [
        "Antiquated Artifact Weapons (Stormblood)", "Eureka Artifact Weapons",
        "Eureka Artifact Weapons +1", "Eureka Artifact Weapons +2", "Anemos Weapons",
        "Pagos Weapons", "Pagos Weapons +1", "Elemental Weapons", "Elemental Weapons +1",
        "Elemental Weapons +2", "Pyros Weapons", "Hydatos Weapons", "Hydatos Weapons +1",
        "Base Eureka Weapons", "Eureka Weapons", "Physeos Weapons"]),
    ("RS", 3, "Resistance Weapons", [
        "Base Resistance Weapons", "Augmented Resistance Weapons", "Recollection Weapons",
        "Law's Order Weapons", "Augmented Law's Order Weapons", "Blade's Weapons"]),
    ("SK", 3, "Skysteel Tools", [
        "Base Skysteel Tools", "Skysteel Tools 1", "Dragonsung Tools",
        "Augmented Dragonsung Tools", "Skysung Tools", "Skybuilders' Tools"]),
    ("MV", 4, "Manderville Weapons", [
        "Base Manderville Weapons", "Amazing Manderville Weapons",
        "Majestic Manderville Weapons", "Mandervillous Weapons"]),
    ("SP", 4, "Splendorous Tools", [
        "Base Splendorous Tools", "Augmented Splendorous Tools", "Crystalline Tools",
        "Chora-Zoi's Crystalline Tools", "Brilliant Tools", "Vrandtic Visionary's Tools",
        "Lodestar Tools"]),
    ("PH", 5, "Phantom Weapons", [
        "Phantom Weapons Penumbrae", "Phantom Weapons Umbrae", "Phantom Weapons Obscurum",
        "Phantom Weapons Eclipticum", "Phantom Weapons Occultum"]),
    ("CO", 5, "Cosmic Tools", [
        "Prototype v0.1 Cosmic Tools", "Prototype v0.2 Cosmic Tools",
        "Prototype v0.3 Cosmic Tools", "Prototype v0.4 Cosmic Tools",
        "Prototype v0.5 Cosmic Tools", "Prototype v0.6 Cosmic Tools",
        "Prototype v0.7 Cosmic Tools", "Prototype v0.8 Cosmic Tools",
        "Cosmic Tools (IL 720)", "Cosmic Tools v1.1", "Cosmic Tools v1.2",
        "Cosmic Tools v1.3", "Cosmic Tools v1.4", "Stellar Tools", "Stellar Tools v1.1",
        "Stellar Tools v1.2", "Hypertools", "Hypertools v1.1", "Hypertools v1.2",
        "Tools of Stars"]),
]

# The wiki's "requires" field names the job in words.
JOBS = {
    "paladin": "PLD", "warrior": "WAR", "dark knight": "DRK", "gunbreaker": "GNB",
    "white mage": "WHM", "scholar": "SCH", "astrologian": "AST", "sage": "SGE",
    "monk": "MNK", "dragoon": "DRG", "ninja": "NIN", "samurai": "SAM", "reaper": "RPR",
    "viper": "VPR", "bard": "BRD", "machinist": "MCH", "dancer": "DNC",
    "black mage": "BLM", "summoner": "SMN", "red mage": "RDM", "pictomancer": "PCT",
    "blue mage": "BLU", "beastmaster": "BST",
    "carpenter": "CRP", "blacksmith": "BSM", "armorer": "ARM", "goldsmith": "GSM",
    "leatherworker": "LTW", "weaver": "WVR", "alchemist": "ALC", "culinarian": "CUL",
    "miner": "MIN", "botanist": "BTN", "fisher": "FSH",
}


def field(raw, key):
    m = re.search(r"\|\s*" + re.escape(key) + r"\s*=\s*([^\n|]*)", raw)
    return m.group(1).strip() if m else ""


def read_sets():
    """set name -> [(title, id, item level, job)] for every relic stage set."""
    wanted = {s for _, _, _, stages in LINES for s in stages}
    sets = {s: [] for s in wanted}
    con = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
    for title, raw in con.execute("select title, raw_text from pages where raw_text like '%set-name%'"):
        name = field(raw, "set-name")
        if name not in wanted:
            continue
        job = JOBS.get(field(raw, "requires").lower())
        item = field(raw, "id-gt")
        if not job or not item.isdigit():
            sys.exit(f"{title}: job '{field(raw, 'requires')}' or id '{item}' not readable")
        sets[name].append((title, int(item), field(raw, "ilvl"), job))
    missing = [s for s, rows in sets.items() if not rows]
    if missing:
        sys.exit(f"No items for: {missing}")
    return sets


def write_cs(sets):
    # An item can have two wiki pages (Cosmic Round Knife v0.8 is also under its
    # old "Prototype v0.8" title). A repeated key would throw when the plugin
    # loads, so each id is written once; two pages that disagree stop the build.
    seen = {}
    rows = []
    for code, _, name, stages in LINES:
        rows.append(f"    // {name}")
        for i, stage in enumerate(stages):
            for title, item, _, job in sorted(sets[stage], key=lambda r: r[1]):
                if item in seen:
                    if seen[item] != (code, job, i):
                        sys.exit(f"{title} ({item}) is {seen[item]} on another page and {(code, job, i)} here")
                    continue
                seen[item] = (code, job, i)
                rows.append(f'    [{item}] = ("{code}", "{job}", {i}),  // {title}')
    CS_OUT.write_text(
        "// Generated by tools/Build-RelicStages.py from the local wiki corpus. Do not edit.\n"
        "namespace TimeMemoria.Services;\n\n"
        "public static class RelicStages\n{\n"
        "  /// <summary>Item id -> (line code, job abbreviation, stage index from 0).</summary>\n"
        "  public static readonly IReadOnlyDictionary<uint, (string Line, string Job, int Stage)> ByItem =\n"
        "    new Dictionary<uint, (string Line, string Job, int Stage)>\n  {\n"
        + "\n".join(rows) + "\n  };\n}\n",
        encoding="utf-8", newline="\n")


def write_js(sets, pl):
    lines = []
    for code, ex, name, stages in LINES:
        parts = []
        for stage in stages:
            ils = sorted({int(r[2]) for r in sets[stage] if r[2].isdigit()})
            parts.append(f'[{js(stage)},{ils[0] if ils else "null"}]')
        lines.append(f'  {code}:{{ex:{ex},name:{js(name)},stages:[{",".join(parts)}]}}')
    (pl / "relic-stages.js").write_text(
        "// Generated by TimeMemoriaV3/tools/Build-RelicStages.py from the local wiki corpus. Do not edit.\n"
        "// Line code -> {expansion, name, stages: [[wiki set name, item level]]}. Stage letter A is index 0,\n"
        "// the letter Time Memoria's collections export sends in \"relics\".\n"
        "const RELIC_STAGES = {\n" + ",\n".join(lines) + "\n};\n",
        encoding="utf-8", newline="\n")


def js(s):
    return '"' + s.replace("\\", "\\\\").replace('"', '\\"') + '"'


if __name__ == "__main__":
    sets = read_sets()
    write_cs(sets)
    print(f"{sum(len(r) for r in sets.values())} items in {len(sets)} stages -> {CS_OUT}")
    if len(sys.argv) > 1:
        write_js(sets, Path(sys.argv[1]))
        print(f"-> {Path(sys.argv[1]) / 'relic-stages.js'}")
