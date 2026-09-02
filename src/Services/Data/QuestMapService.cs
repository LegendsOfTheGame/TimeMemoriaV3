namespace TimeMemoria.Services;

public interface IQuestMapService
{
  /// <summary>
  /// Where a quest is picked up, formatted the way the game's own map would
  /// show it ("Foundation (13.2, 12.5)"). Null when none of the quest's ids
  /// resolve to a location.
  /// </summary>
  string? GetIssuerLabel(Types.Quest quest);

  /// <summary>Opens the native map flagged on the quest's issuer. False when
  /// nothing resolved — nothing to open.</summary>
  bool OpenIssuerOnMap(Types.Quest quest);
}

/// <summary>
/// Where a quest is picked up, resolved fresh from Lumina on every call rather
/// than cached on <see cref="Types.Quest"/> — a flag click is rare enough that
/// the lookup cost is free, and most rows will never use this.
///
/// Handles the same IssuerLocation/LevelLevemete split Classic's old
/// <c>OpenAreaMap</c> did (a levequest has no IssuerLocation; its location
/// comes from the Leve sheet instead) — but that code passed raw world
/// position straight into <c>MapLinkPayload</c>'s <c>(int rawX, int rawY)</c>
/// constructor, which expects the SeString wire-format encoding, not world
/// units × 1000. The two are unrelated scales, so it never opened on the
/// right spot. This uses <see cref="MapUtil.WorldToMap"/> to get the actual
/// human map coordinate first, then the constructor that takes exactly that
/// ("nice" coordinates) — verified against Quest Tracker and the wiki on
/// three separate quests via MemoriaProbe's <c>questmap</c> probe.
/// </summary>
public class QuestMapService(IDataManager _dataManager, IGameGui _gameGui) : IQuestMapService
{
  /// <summary>
  /// Zones with no viewable map graphic in the vanilla client — the flag
  /// would only ever open to "Unable to display area." There is no Lumina
  /// field that flags this, so the list is grown by hand from user reports
  /// rather than derived. One name here suppresses every quest issued from
  /// that room, not just the one that surfaced it.
  /// </summary>
  private static readonly HashSet<string> ExcludedZones = new(StringComparer.Ordinal)
  {
    "Fortemps Manor"
  };

  public string? GetIssuerLabel(Types.Quest quest)
  {
    return TryResolve(quest, out Level level, out Vector3 coord) ? Format(level, coord) : null;
  }

  public bool OpenIssuerOnMap(Types.Quest quest)
  {
    if (!TryResolve(quest, out Level level, out Vector3 coord)) return false;

    MapLinkPayload link = new(level.Territory.RowId, level.Map.RowId, coord.X, coord.Y);
    _gameGui.OpenMapWithMapLink(link);
    return true;
  }

  /// <summary>
  /// Finds the location by hand rather than chaining <c>FirstOrDefault</c>
  /// straight into a property read. <c>FirstOrDefault</c> returns a
  /// zeroed-out default struct when nothing matches, and reading
  /// <c>IssuerLocation</c>/<c>LevelLevemete</c> off that (rather than a real
  /// row from the sheet) throws inside Lumina's generated code — which a quest
  /// with no resolvable location among any of its ids hits routinely, not as
  /// an edge case.
  /// </summary>
  private bool TryResolve(Types.Quest quest, out Level level, out Vector3 coord)
  {
    Level? found = null;

    if (quest.IsLeve)
    {
      foreach (Leve leve in _dataManager.GetExcelSheet<Leve>())
      {
        if (!quest.Ids.Contains(leve.RowId)) continue;
        if (leve.LevelLevemete.ValueNullable is not Level location) continue;
        found = location;
        break;
      }
    }
    else
    {
      foreach (Lumina.Excel.Sheets.Quest row in _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>())
      {
        if (!quest.Ids.Contains(row.RowId)) continue;
        if (row.IssuerLocation.ValueNullable is not Level location) continue;
        found = location;
        break;
      }
    }

    if (found is not Level resolved || ExcludedZones.Contains(resolved.Territory.Value.PlaceName.Value.Name.ToString()))
    {
      level = default;
      coord = default;
      return false;
    }

    level = resolved;

    TerritoryTypeTransient transient = _dataManager.GetExcelSheet<TerritoryTypeTransient>().GetRow(level.Territory.RowId);
    coord = MapUtil.WorldToMap(new Vector3(level.X, level.Y, level.Z), level.Map.Value, transient);
    return true;
  }

  /// <summary>The number the player actually reads: "Foundation (13.2, 12.5)".</summary>
  private static string Format(Level level, Vector3 coord)
  {
    string zone = level.Territory.Value.PlaceName.Value.Name.ToString();
    return $"{zone} ({coord.X:F1}, {coord.Y:F1})";
  }
}
