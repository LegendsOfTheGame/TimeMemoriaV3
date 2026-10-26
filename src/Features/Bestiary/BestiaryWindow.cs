namespace TimeMemoria.Features.Bestiary;

/// <summary>
/// Always the classic window, like <c>/tmwiki</c>: a sortable table gains
/// nothing from the game's styling, and ImGui gives sorting and resizable
/// columns for free.
/// </summary>
public class BestiaryWindow : Window
{
  private static readonly Vector4 CapturedColour = new(0.45f, 0.9f, 0.45f, 1.0f);

  private readonly BestiaryFeature _feature;
  private readonly List<Beast> _beasts;

  private string _search = "";

  /// <summary>Size is set here and not in Draw; see WikiWindow for why.</summary>
  public BestiaryWindow(BestiaryFeature feature, List<Beast> beasts)
    : base("Tales of Milestones — Master's Bestiary##TimeMemoriaBestiary")
  {
    _feature = feature;
    _beasts = beasts;

    Size = new Vector2(820, 520);
    SizeCondition = ImGuiCond.FirstUseEver;
    SizeConstraints = new()
    {
      MinimumSize = new Vector2(520, 260),
      MaximumSize = new Vector2(float.MaxValue)
    };
  }

  public override void Draw()
  {
    if (_beasts.Count == 0)
    {
      ImGui.TextDisabled("The beast list could not be loaded. See /xllog for the reason.");
      return;
    }

    bool ready = _feature.CaptureStateReady;
    if (ready)
    {
      int captured = _beasts.Count(_feature.IsCaptured);
      ImGui.TextUnformatted($"Captured {captured} of {_beasts.Count}");
    }
    else
    {
      ImGui.TextDisabled("Capture state is not loaded yet. Open the Master's Bestiary in game once to load it.");
    }

    ImGui.SetNextItemWidth(-1);
    ImGui.InputTextWithHint("##bestiarySearch", "Search by name, enemy or zone", ref _search, 128);

    List<Beast> rows = string.IsNullOrWhiteSpace(_search)
      ? [.. _beasts]
      : [.. _beasts.Where((b) =>
          b.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
          b.Source.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
          b.LocationText.Contains(_search, StringComparison.OrdinalIgnoreCase))];

    using ImRaii.TableDisposable table = ImRaii.Table("##bestiaryTable", 8,
      ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
      ImGuiTableFlags.Resizable | ImGuiTableFlags.Sortable);
    if (!table.Success) return;

    ImGui.TableSetupScrollFreeze(0, 1);
    ImGui.TableSetupColumn("No.", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort, 32);
    ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 110);
    ImGui.TableSetupColumn("Tamed From", ImGuiTableColumnFlags.WidthFixed, 140);
    ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch);
    ImGui.TableSetupColumn("Lvl", ImGuiTableColumnFlags.WidthFixed, 32);
    ImGui.TableSetupColumn("Captured", ImGuiTableColumnFlags.WidthFixed, 68);
    ImGui.TableSetupColumn("Map", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 40);
    ImGui.TableSetupColumn("Kornago Gourd", ImGuiTableColumnFlags.WidthFixed, 180);
    ImGui.TableHeadersRow();

    Sort(rows);

    foreach (Beast beast in rows)
    {
      ImGui.TableNextRow();

      ImGui.TableNextColumn();
      ImGui.TextUnformatted(beast.No.ToString());

      ImGui.TableNextColumn();
      ImGui.TextUnformatted(beast.Name);

      ImGui.TableNextColumn();
      ImGui.TextUnformatted(beast.Source.Length > 0 ? beast.Source : "--");

      ImGui.TableNextColumn();
      ImGui.TextWrapped(beast.LocationText);

      ImGui.TableNextColumn();
      ImGui.TextUnformatted(beast.MinLevel.ToString());

      ImGui.TableNextColumn();
      if (!ready) ImGui.TextDisabled("?");
      else if (_feature.IsCaptured(beast)) ImGui.TextColored(CapturedColour, "Captured");
      else ImGui.TextDisabled("--");

      ImGui.TableNextColumn();
      if (beast.HasMapLocation)
      {
        if (ImGui.SmallButton($"Flag##{beast.No}")) _feature.OpenOnMap(beast);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open the map at this spot.");
      }
      else
      {
        ImGui.TextDisabled("--");
      }

      ImGui.TableNextColumn();
      ImGui.TextWrapped(beast.Gourd ?? "--");
    }
  }

  /// <summary>Sorted every frame: 50 rows cost nothing, and the Captured
  /// column can change while the window is open.</summary>
  private void Sort(List<Beast> rows)
  {
    ImGuiTableSortSpecsPtr specs = ImGui.TableGetSortSpecs();
    if (specs.SpecsCount == 0) return;

    ImGuiTableColumnSortSpecsPtr spec = specs.Specs;
    bool descending = spec.SortDirection == ImGuiSortDirection.Descending;

    Comparison<Beast> compare = spec.ColumnIndex switch
    {
      1 => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
      2 => (a, b) => string.Compare(a.Source, b.Source, StringComparison.OrdinalIgnoreCase),
      3 => (a, b) => string.Compare(a.LocationText, b.LocationText, StringComparison.OrdinalIgnoreCase),
      4 => (a, b) => a.MinLevel.CompareTo(b.MinLevel),
      5 => (a, b) => _feature.IsCaptured(a).CompareTo(_feature.IsCaptured(b)),
      7 => (a, b) => string.Compare(a.Gourd ?? "", b.Gourd ?? "", StringComparison.OrdinalIgnoreCase),
      _ => (a, b) => a.No.CompareTo(b.No),
    };

    rows.Sort((a, b) => descending ? -compare(a, b) : compare(a, b));
  }
}
