using QuestRow = Lumina.Excel.Sheets.Quest;

namespace TimeMemoria.Windows;

/// <summary>
/// <c>/tmwiki</c>: every quest in the journal right now, each one a click away
/// from its wiki page. For the moment mid-fight when somebody asks what a quest
/// was about and the answer is "let me look it up".
///
/// Always the classic window, whichever window is chosen in Settings. A list of
/// links has nothing to gain from the game's own styling, and one window class
/// means one thing to keep working.
/// </summary>
public unsafe class WikiWindow : Window
{
  private static readonly Vector4 HeaderColour = new(0.5f, 0.8f, 1.0f, 1.0f);

  // PlaceName 385 is "First Dicasterial Observatorium of Aetherial and
  // Astrological Phenomena", 388 is "Camp Dragonhead" (read from the sheet,
  // 18 Nov 2026).
  private const uint ObservatoriumPlaceId = 385;
  private const uint CampDragonheadPlaceId = 388;

  private readonly IDataManager _dataManager;

  private readonly record struct Accepted(uint Id, string Name, uint ExpansionId, string Expansion, string Place, string Story, string Tribe);

  private readonly record struct HeldLeve(uint Id, string Name, string Place, string Job);

  /// <summary>
  /// The default size is set here, not with ImGui.SetNextWindowSize in Draw.
  /// Draw runs after the window has begun, so a SetNextWindowSize there lands
  /// on the list's child instead, and the window sizes itself to its content.
  /// </summary>
  public WikiWindow(IDataManager dataManager) : base("Tales of Milestones — Wiki##TimeMemoriaWikiList")
  {
    _dataManager = dataManager;

    // The /tmmini window's size (NativeUiService), so the two small windows
    // match when both are open.
    Size = new Vector2(310, 400);
    SizeCondition = ImGuiCond.FirstUseEver;
    SizeConstraints = new()
    {
      MinimumSize = new Vector2(260, 160),
      MaximumSize = new Vector2(float.MaxValue)
    };
  }

  public override void Draw()
  {
    List<Accepted> quests = ReadJournal();
    List<HeldLeve> leves = ReadLeves();

    if (quests.Count == 0 && leves.Count == 0)
    {
      ImGui.TextDisabled("Your journal has no accepted quests.");
      ImGui.Spacing();
      if (ImGui.Button("Open the Main Scenario index")) MainWindow.OpenWikiSearch("");
      return;
    }

    // Wrapped: at /tmmini's width the line is longer than the window.
    string count = leves.Count == 0
      ? $"Accepted quests ({quests.Count})."
      : $"Accepted quests ({quests.Count}) and levequests ({leves.Count}).";
    using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled)))
      ImGui.TextWrapped($"{count} Click a quest to open its wiki page.");
    ImGui.Separator();

    using ImRaii.ChildDisposable child = ImRaii.Child("##wikiQuests", ImGuiHelpers.ScaledVector2(0), false);
    if (!child.Success) return;

    // Expansion first, oldest at the top, then the journal's own place headings
    // underneath. The journal orders its places by a rule nobody has pinned
    // down, so places keep the order their first quest holds in the journal
    // slots rather than a guessed copy of the journal's.
    foreach (IGrouping<uint, Accepted> expansion in quests.GroupBy((q) => q.ExpansionId).OrderBy((g) => g.Key))
    {
      ImGui.Spacing();
      // Each expansion has two story pages on the wiki, "<X> Main Scenario
      // Quests" and "Post-<X> Main Scenario Quests". An accepted MSQ names the
      // arc it belongs to, so the heading follows it; with no MSQ held there, it
      // falls back to the expansion's own page. Going through the search means
      // an expansion with no page yet lands on results rather than a missing page.
      string name = expansion.First().Expansion;
      string page = expansion.Select((q) => q.Story).FirstOrDefault((s) => s.Length > 0)
        ?? $"{name} Main Scenario Quests";

      ImGui.TextColored(HeaderColour, name);
      if (ImGui.IsItemHovered())
      {
        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        ImGui.SetTooltip($"Open \"{page}\" on the FFXIV wiki.");
      }
      if (ImGui.IsItemClicked()) MainWindow.OpenWikiSearch(page);

      foreach (IGrouping<string, Accepted> place in expansion.Where((q) => q.Tribe.Length == 0).GroupBy((q) => q.Place))
        DrawGroup(place.Key, place);

      // Allied society quests are dailies, up to three per society, and they
      // pushed the story quests apart when they shared the place headings. One
      // heading for the lot, then a sub-heading per society, by its name.
      Accepted[] tribal = expansion.Where((q) => q.Tribe.Length > 0).ToArray();
      if (tribal.Length > 0)
      {
        ImGui.TextDisabled("  Allied Society Quests");
        using ImRaii.IndentDisposable indent = ImRaii.PushIndent();
        foreach (IGrouping<string, Accepted> tribe in tribal.GroupBy((q) => q.Tribe))
          DrawGroup(tribe.Key, tribe);
      }
    }

    if (leves.Count == 0) return;

    // Leves sit apart from the expansions, the way the journal gives them a
    // tab of their own, and group by the levemete's town. The wiki has a page
    // per leve under its own name, so the search works the same as for quests.
    ImGui.Spacing();
    ImGui.TextColored(HeaderColour, "Levequests");
    if (ImGui.IsItemHovered())
    {
      ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
      ImGui.SetTooltip("Open \"Levequests\" on the FFXIV wiki.");
    }
    if (ImGui.IsItemClicked()) MainWindow.OpenWikiSearch("Levequests");

    foreach (IGrouping<string, HeldLeve> place in leves.GroupBy((l) => l.Place))
    {
      if (place.Key.Length > 0) ImGui.TextDisabled($"  {place.Key}");

      using ImRaii.IndentDisposable indent = ImRaii.PushIndent();
      foreach (HeldLeve leve in place)
      {
        if (ImGui.Selectable($"{leve.Name}##leve{leve.Id}")) MainWindow.OpenWikiSearch(leve.Name);
        if (ImGui.IsItemHovered())
          ImGui.SetTooltip(leve.Job.Length > 0 ? $"{leve.Job}. Open this leve on the FFXIV wiki." : "Open this leve on the FFXIV wiki.");
      }
    }
  }

  private static void DrawGroup(string heading, IEnumerable<Accepted> quests)
  {
    if (heading.Length > 0) ImGui.TextDisabled($"  {heading}");

    using ImRaii.IndentDisposable indent = ImRaii.PushIndent();
    foreach (Accepted quest in quests)
    {
      // The id goes in the label so two quests with the same name — the
      // game has a few — stay separate selectables.
      if (ImGui.Selectable($"{quest.Name}##{quest.Id}")) MainWindow.OpenWikiSearch(quest.Name);
      if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open this quest on the FFXIV wiki.");
    }
  }

  /// <summary>
  /// Read every frame while the window is open. The journal holds at most thirty
  /// entries, so there is nothing worth caching, and a quest accepted mid-fight
  /// shows up without a refresh.
  ///
  /// QuestWork holds the id less 0x10000; the Quest sheet holds the full id.
  /// Quests hidden in the journal are listed too — hiding a quest there is about
  /// tidying the tracker, not about not wanting to read up on it.
  /// </summary>
  private List<Accepted> ReadJournal()
  {
    List<Accepted> quests = [];

    QuestManager* manager = QuestManager.Instance();
    if (manager is null) return quests;

    foreach (ref readonly var work in manager->NormalQuests)
    {
      if (work.QuestId == 0) continue;

      uint id = work.QuestId + 0x10000u;
      if (_dataManager.GetExcelSheet<QuestRow>().GetRowOrDefault(id) is not { } row) continue;

      string name = row.Name.ToString();
      if (name.Length == 0) continue;

      quests.Add(new Accepted(id, name,
        row.Expansion.RowId,
        row.Expansion.ValueNullable?.Name.ToString() ?? "",
        row.PlaceName.ValueNullable?.Name.ToString() ?? "",
        StoryPage(row),
        // Quest.BeastTribe is the same mark AlliedSocietyService counts by.
        row.BeastTribe.RowId == 0 ? "" : TribeName(row)));
    }

    return quests;
  }

  /// <summary>
  /// The society's name by the same rule as AlliedSocietyService.SocietyName:
  /// NameRelation ("Ixali Relations") with the " Relations" suffix taken off.
  /// </summary>
  private static string TribeName(QuestRow row)
  {
    const string suffix = " Relations";

    string name = row.BeastTribe.ValueNullable?.NameRelation.ToString() ?? "";
    if (name.Length == 0) return "Allied Society";
    return name.EndsWith(suffix, StringComparison.Ordinal) ? name[..^suffix.Length] : name;
  }

  /// <summary>
  /// The held leves: the same list as the journal's Levequests tab, which
  /// NormalQuests does not hold. LeveWork carries the Leve sheet's row id.
  /// </summary>
  private List<HeldLeve> ReadLeves()
  {
    List<HeldLeve> leves = [];

    QuestManager* manager = QuestManager.Instance();
    if (manager is null) return leves;

    foreach (ref readonly var work in manager->LeveQuests)
    {
      if (work.LeveId == 0) continue;
      if (_dataManager.GetExcelSheet<Lumina.Excel.Sheets.Leve>().GetRowOrDefault(work.LeveId) is not { } row) continue;

      string name = row.Name.ToString();
      if (name.Length == 0) continue;

      // The Observatorium is a building inside Camp Dragonhead, and players
      // know its levemete by the camp's name. Its own name is also too long
      // for the window. Both ids are PlaceName rows, so the swap keeps the
      // client's language.
      uint place = row.PlaceNameStart.RowId == ObservatoriumPlaceId ? CampDragonheadPlaceId : row.PlaceNameStart.RowId;

      leves.Add(new HeldLeve(work.LeveId, name,
        _dataManager.GetExcelSheet<Lumina.Excel.Sheets.PlaceName>().GetRowOrDefault(place)?.Name.ToString() ?? "",
        row.LeveAssignmentType.ValueNullable?.Name.ToString() ?? ""));
    }

    return leves;
  }

  /// <summary>
  /// The journal's MSQ categories are named exactly as the wiki titles its story
  /// pages — "Post-Shadowbringers Main Scenario Quests", "Post-Dawntrail Main
  /// Scenario Quests II" — and every one resolves there, some as redirects.
  /// Checked against the sheet and the wiki on 11 October 2026.
  ///
  /// Matched on the name, not a section id: the wiki is English, so a name
  /// that is not a wiki title is no use as a link however it was found, and an
  /// empty result falls back to the expansion's own page.
  /// </summary>
  private static string StoryPage(QuestRow row)
  {
    string category = row.JournalGenre.ValueNullable?.JournalCategory.ValueNullable?.Name.ToString() ?? "";
    return category.EndsWith("Main Scenario Quests", StringComparison.Ordinal) ? category : "";
  }
}
