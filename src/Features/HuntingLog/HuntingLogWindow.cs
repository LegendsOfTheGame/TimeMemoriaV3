namespace TimeMemoria.Features.HuntingLog;

/// <summary>
/// Always the classic window, like the Bestiary table: a sortable table gains
/// nothing from the game's styling.
/// </summary>
public class HuntingLogWindow : Window
{
  private static readonly Vector4 CurrentColour = new(0.45f, 0.9f, 0.45f, 1.0f);
  private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

  private readonly HuntingLogFeature _feature;

  private string _search = "";
  private bool _currentOnly;
  private bool _zoneOnly;

  /// <summary>Read once a second, not every frame: kills change slowly, and
  /// a read walks up to 120 sheet rows.</summary>
  private List<HuntLogState> _logs = [];
  private DateTime _lastRead = DateTime.MinValue;

  /// <summary>Base class to its gear sets, read with the logs.</summary>
  private Dictionary<uint, List<HuntGearset>> _gearsets = [];

  /// <summary>Read again on the next frame: the class or the zone changed.</summary>
  public void RequestRefresh() => _lastRead = DateTime.MinValue;

  /// <summary>Size is set here and not in Draw; see WikiWindow for why.</summary>
  public HuntingLogWindow(HuntingLogFeature feature)
    : base("Tales of Milestones — Hunting Log##TimeMemoriaHuntingLog")
  {
    _feature = feature;

    Size = new Vector2(860, 520);
    SizeCondition = ImGuiCond.FirstUseEver;
    SizeConstraints = new()
    {
      MinimumSize = new Vector2(560, 260),
      MaximumSize = new Vector2(float.MaxValue)
    };
  }

  public override void Draw()
  {
    if (!_feature.IsLoggedIn)
    {
      ImGui.TextDisabled("Log in to a character to see its Hunting Log.");
      return;
    }

    DateTime now = DateTime.UtcNow;
    if (now - _lastRead >= RefreshInterval)
    {
      _logs = _feature.ReadLogs();
      _gearsets = _logs.Where(l => l.ClassId != 0).ToDictionary(l => l.ClassId, l => _feature.GearsetsFor(l.ClassId));
      _lastRead = now;
    }

    if (_logs.Count == 0)
    {
      ImGui.TextDisabled("The Hunting Log is not loaded yet.");
      return;
    }

    DrawSummary();

    ImGui.Checkbox("Current class only", ref _currentOnly);
    ImGui.SameLine();
    ImGui.Checkbox("Current zone only", ref _zoneOnly);
    ImGui.SameLine();
    ImGui.SetNextItemWidth(-1);
    ImGui.InputTextWithHint("##huntSearch", "Search by class, monster or zone", ref _search, 128);

    List<HuntTarget> rows = [.. _logs
      .Where(l => !_currentOnly || l.IsCurrent)
      .SelectMany(l => l.Needed)
      .Where(t => !_zoneOnly || _feature.IsInCurrentZone(t))
      .Where(Matches)];

    if (rows.Count == 0)
    {
      ImGui.TextDisabled(_logs.All(l => l.IsComplete)
        ? "Every Hunting Log on this character is complete."
        : _zoneOnly && string.IsNullOrWhiteSpace(_search)
          ? "No monster you still need is in this zone."
          : "No monster matches.");
      return;
    }

    DrawTable(rows);
  }

  /// <summary>One line for each log: where it stands, in the game's own terms.</summary>
  private void DrawSummary()
  {
    foreach (HuntLogState log in _logs)
    {
      string text = log.IsComplete
        ? $"{log.Name}: complete"
        : $"{log.Name}: rank {log.Rank + 1} of {log.RankCount}, {log.TasksDone}/{log.TasksInRank}";
      if (log.IsCurrent) ImGui.TextColored(CurrentColour, text);
      else ImGui.TextUnformatted(text);

      DrawGearsetClick(log);

      // Wrap the summary onto as many lines as the window width needs.
      if (log != _logs[^1])
      {
        float next = ImGui.CalcTextSize(text).X + ImGui.GetStyle().ItemSpacing.X * 4;
        if (ImGui.GetItemRectMax().X + next < ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X)
        {
          ImGui.SameLine();
          ImGui.TextDisabled("·");
          ImGui.SameLine();
        }
      }
    }
    ImGui.Spacing();
  }

  /// <summary>
  /// Prime Directive amendment of 22 Nov 2026: only these summary names change
  /// class, nothing else in the window. One set: the click equips it. Several
  /// (say a Pugilist and a Monk set): the click opens a list and the player
  /// picks; the plugin never chooses. None, or the class already worn: the
  /// name is plain text.
  /// </summary>
  private void DrawGearsetClick(HuntLogState log)
  {
    if (log.IsCurrent || !_gearsets.TryGetValue(log.ClassId, out List<HuntGearset>? sets) || sets.Count == 0) return;

    string popup = $"##huntGearsets{log.ClassId}";
    if (ImGui.IsItemHovered())
    {
      ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
      ImGui.SetTooltip(sets.Count == 1
        ? $"Click to change to gear set {sets[0].Label}."
        : $"Click to choose one of {sets.Count} gear sets for {log.Name}.");
    }

    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
    {
      if (sets.Count == 1) _feature.Equip(sets[0]);
      else ImGui.OpenPopup(popup);
    }

    using var list = ImRaii.Popup(popup);
    if (!list.Success) return;
    foreach (HuntGearset set in sets)
    {
      if (ImGui.Selectable(set.Label)) _feature.Equip(set);
    }
  }

  private bool Matches(HuntTarget target)
  {
    if (string.IsNullOrWhiteSpace(_search)) return true;
    return target.Log.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
      || target.Task.Contains(_search, StringComparison.OrdinalIgnoreCase)
      || target.Monster.Contains(_search, StringComparison.OrdinalIgnoreCase)
      || target.Locations.Any(l => _feature.Describe(l).Contains(_search, StringComparison.OrdinalIgnoreCase));
  }

  private void DrawTable(List<HuntTarget> rows)
  {
    using ImRaii.TableDisposable table = ImRaii.Table("##huntTable", 6,
      ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
      ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable);
    if (!table.Success) return;

    ImGui.TableSetupScrollFreeze(0, 1);
    ImGui.TableSetupColumn("Class", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort, 110);
    ImGui.TableSetupColumn("Task", ImGuiTableColumnFlags.WidthFixed, 120);
    ImGui.TableSetupColumn("Monster", ImGuiTableColumnFlags.WidthFixed, 160);
    ImGui.TableSetupColumn("Kills", ImGuiTableColumnFlags.WidthFixed, 44);
    ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch);
    ImGui.TableSetupColumn("Map", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 40);
    ImGui.TableHeadersRow();

    Sort(rows);

    int index = 0;
    foreach (HuntTarget target in rows)
    {
      index++;
      HuntLocation? location = _feature.PreferredLocation(target);

      ImGui.TableNextRow();

      ImGui.TableNextColumn();
      if (target.Log.IsCurrent) ImGui.TextColored(CurrentColour, target.Log.Name);
      else ImGui.TextUnformatted(target.Log.Name);

      ImGui.TableNextColumn();
      ImGui.TextUnformatted(target.Task);

      ImGui.TableNextColumn();
      ImGui.TextUnformatted(target.Monster);

      ImGui.TableNextColumn();
      ImGui.TextUnformatted($"{target.Killed}/{target.Needed}");

      ImGui.TableNextColumn();
      if (location is null)
      {
        ImGui.TextDisabled("--");
      }
      else
      {
        ImGui.TextWrapped(_feature.Describe(location));
        if (target.Locations.Count > 1 && ImGui.IsItemHovered())
          ImGui.SetTooltip("Also found at:\n" + string.Join("\n", target.Locations.Where(l => l != location).Select(_feature.Describe)));
      }

      ImGui.TableNextColumn();
      if (location is not null)
      {
        if (ImGui.SmallButton($"Flag##hunt{index}")) _feature.OpenOnMap(location);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open the map at this spot.");
      }
      else
      {
        ImGui.TextDisabled("--");
      }
    }
  }

  private void Sort(List<HuntTarget> rows)
  {
    ImGuiTableSortSpecsPtr specs = ImGui.TableGetSortSpecs();

    Comparison<HuntTarget> byLog = (a, b) => a.Log.Order != b.Log.Order
      ? a.Log.Order.CompareTo(b.Log.Order)
      : a.TaskOrder.CompareTo(b.TaskOrder);

    if (specs.SpecsCount == 0)
    {
      rows.Sort(byLog);
      return;
    }

    ImGuiTableColumnSortSpecsPtr spec = specs.Specs;
    bool descending = spec.SortDirection == ImGuiSortDirection.Descending;

    Comparison<HuntTarget> compare = spec.ColumnIndex switch
    {
      1 => (a, b) => string.Compare(a.Task, b.Task, StringComparison.OrdinalIgnoreCase),
      2 => (a, b) => string.Compare(a.Monster, b.Monster, StringComparison.OrdinalIgnoreCase),
      3 => (a, b) => (a.Needed - a.Killed).CompareTo(b.Needed - b.Killed),
      4 => (a, b) => string.Compare(LocationText(a), LocationText(b), StringComparison.OrdinalIgnoreCase),
      _ => byLog,
    };

    rows.Sort((a, b) =>
    {
      int result = descending ? -compare(a, b) : compare(a, b);
      return result != 0 ? result : byLog(a, b);
    });
  }

  private string LocationText(HuntTarget target) =>
    _feature.PreferredLocation(target) is HuntLocation location ? _feature.Describe(location) : "";
}
