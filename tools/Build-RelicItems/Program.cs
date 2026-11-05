// Writes tools/relic-items.tsv: the relic items that Build-RelicStages.py cannot read
// from the wiki's stage sets. Each row is: item id, line code, key, stage, item name.
//
// FFXIV Collect's API gives the items and their groups. The game's sheets give each
// item's slot and job, and the script stops if a name differs between the two.
//
// Run it when a patch adds relic items, then run Build-RelicStages.py:
//   dotnet run --project tools/Build-RelicItems -- "C:\FFXIV\game\sqpack" tools/relic-items.tsv
using System.Text.Json;
using System.Text.RegularExpressions;
using Lumina;
using Lumina.Excel.Sheets;

string sqpack = args.Length > 0 ? args[0] : @"C:\FFXIV\game\sqpack";
string output = args.Length > 1 ? args[1] : "tools/relic-items.tsv";

// FFXIV Collect type -> line code and stage. A stage's letter is its place: A is 0.
var armorRole = new Dictionary<string, (string Line, int Stage)>
{
  ["Elemental Armor"] = ("E", 0), ["Elemental Armor +1"] = ("E", 1), ["Elemental Armor +2"] = ("E", 2),
  ["Arcanaut's Armor"] = ("A", 0), ["Arcanaut's Armor +1"] = ("A", 1), ["Arcanaut's Armor +2"] = ("A", 2),
  ["Phantom Vision"] = ("P", 0), ["Phantom Vision +1"] = ("P", 1), ["Phantom Vision +2"] = ("P", 2), ["Phantom Vision +3"] = ("P", 3),
  ["Bozjan Armor"] = ("B", 0), ["Augmented Bozjan Armor"] = ("B", 1),
  ["Law's Order"] = ("L", 0), ["Augmented Law's Order"] = ("L", 1),
  ["Blade's Armor"] = ("D", 0),
};
var armorJob = new Dictionary<string, (string Line, int Stage)>
{
  ["Eureka Job Armor"] = ("J", 0), ["Eureka Job Armor +1"] = ("J", 1), ["Eureka Job Armor +2"] = ("J", 2), ["Eureka Anemos Armor"] = ("J", 3),
  ["Idealized Armor"] = ("I", 0),
};
var weapons = new Dictionary<string, string> { ["Figmental Weapons"] = "FG", ["Exquisite Weapons"] = "EX", ["GARO Weapons"] = "GW" };
var roles = new Dictionary<string, string>
{
  ["Fending"] = "F", ["Maiming"] = "M", ["Striking"] = "S", ["Aiming"] = "A", ["Scouting"] = "O", ["Healing"] = "H", ["Casting"] = "C",
};

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("relic-table-builder");
using var doc = JsonDocument.Parse(await http.GetStringAsync("https://ffxivcollect.com/api/relics?limit=5000"));

var data = new GameData(sqpack);
var items = data.GetExcelSheet<Item>()!;

string Slot(Item it)
{
  var e = it.EquipSlotCategory.Value;
  return e.Head != 0 ? "H" : e.Body != 0 ? "B" : e.Gloves != 0 ? "G" : e.Legs != 0 ? "L" : e.Feet != 0 ? "F"
    : throw new InvalidDataException($"{it.Name}: no armor slot");
}
// "GLA PLD" is the class and job. The job is the last word.
string Job(Item it) => it.ClassJobCategory.Value.Name.ToString().Split(' ')[^1];

var rows = new List<(uint Id, string Line, string Key, int Stage, string Name)>();
var deepDungeon = new List<(int Order, uint Id, string Name)>();
var garoArmor = new List<(int Order, uint Id, string Name, string Slot)>();

foreach (var r in doc.RootElement.GetProperty("results").EnumerateArray())
{
  uint id = r.GetProperty("id").GetUInt32();
  string name = r.GetProperty("name").GetString()!, type = r.GetProperty("type").GetProperty("name").GetString()!;
  bool wanted = armorRole.ContainsKey(type) || armorJob.ContainsKey(type) || weapons.ContainsKey(type)
    || type is "Deep Dungeon Weapons" or "GARO Armor";
  if (!wanted) continue;

  var it = items.GetRow(id);
  if (!string.Equals(it.Name.ToString(), name, StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException($"{id}: FFXIV Collect says '{name}', the game says '{it.Name}'");

  if (armorRole.TryGetValue(type, out var ar))
    rows.Add((id, ar.Line, Slot(it) + roles[Regex.Match(name, " of (\\w+)").Groups[1].Value], ar.Stage, name));
  else if (armorJob.TryGetValue(type, out var aj))
    rows.Add((id, aj.Line, Slot(it) + Job(it), aj.Stage, name));
  else if (weapons.TryGetValue(type, out var line))
    rows.Add((id, line, Job(it), 0, name));
  else if (type == "Deep Dungeon Weapons")
    deepDungeon.Add((r.GetProperty("order").GetInt32(), id, name));
  else
    garoArmor.Add((r.GetProperty("order").GetInt32(), id, name, Slot(it)));
}

// Deep Dungeon: FFXIV Collect orders the items stage by stage, 21 jobs at a time.
var ordered = deepDungeon.OrderBy(d => d.Order).ToList();
if (ordered.Count % 21 != 0) throw new InvalidDataException($"Deep Dungeon has {ordered.Count} items");
for (int i = 0; i < ordered.Count; i++)
  rows.Add((ordered[i].Id, "DD", Job(items.GetRow(ordered[i].Id)), i / 21, ordered[i].Name));

// GARO armor has no stages: each of the 17 sets is a letter. FFXIV Collect orders the
// items set by set, 5 pieces at a time. The names follow no pattern across the sets.
var garoOrdered = garoArmor.OrderBy(g => g.Order).ToList();
if (garoOrdered.Count % 5 != 0) throw new InvalidDataException($"GARO armor has {garoOrdered.Count} items");
for (int i = 0; i < garoOrdered.Count; i++)
  rows.Add((garoOrdered[i].Id, "G", garoOrdered[i].Slot, i / 5, garoOrdered[i].Name));
for (int s = 0; s < garoOrdered.Count / 5; s++)
  if (string.Concat(garoOrdered.Skip(s * 5).Take(5).Select(g => g.Slot).Order()) != "BFGHL")
    throw new InvalidDataException($"GARO set {s} is not one piece per slot");

var keys = rows.GroupBy(r => (r.Line, r.Key, r.Stage)).Where(g => g.Count() > 1).ToList();
if (keys.Count > 0) throw new InvalidDataException($"Two items share a code: {string.Join(", ", keys.Select(k => k.First().Name))}");

File.WriteAllLines(output, rows.OrderBy(r => r.Line, StringComparer.Ordinal).ThenBy(r => r.Stage).ThenBy(r => r.Key, StringComparer.Ordinal)
  .Select(r => $"{r.Id}\t{r.Line}\t{r.Key}\t{r.Stage}\t{r.Name}"));
Console.WriteLine($"{rows.Count} items -> {output}");
foreach (var g in rows.GroupBy(r => r.Line).OrderBy(g => g.Key, StringComparer.Ordinal))
  Console.WriteLine($"  {g.Key}: {g.Count()} items, {g.Select(r => r.Key).Distinct().Count()} keys, {g.Select(r => r.Stage).Distinct().Count()} stages");

// Which Deep Dungeon stages are made from another stage.
var stageOf = rows.Where(r => r.Line == "DD").ToDictionary(r => r.Id, r => r.Stage);
var edges = new Dictionary<(int, int), HashSet<uint>>();
foreach (var s in data.GetExcelSheet<SpecialShop>()!)
  foreach (var e in s.Item)
    foreach (var rx in e.ReceiveItems)
      if (stageOf.TryGetValue(rx.Item.RowId, out int to))
        foreach (var c in e.ItemCosts)
          if (stageOf.TryGetValue(c.ItemCost.RowId, out int from))
          {
            if (!edges.TryGetValue((from, to), out var set)) edges[(from, to)] = set = [];
            set.Add(rx.Item.RowId);
          }
foreach (var kv in edges.OrderBy(k => k.Key)) Console.WriteLine($"DD stage {kv.Key.Item1} -> {kv.Key.Item2}: {kv.Value.Count} items");
Console.WriteLine("DD stage names: " + string.Join(" | ", Enumerable.Range(0, ordered.Count / 21).Select(i => ordered[i * 21].Name)));
