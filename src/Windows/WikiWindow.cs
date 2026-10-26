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

  private readonly IDataManager _dataManager;

  private readonly record struct Accepted(uint Id, string Name, uint ExpansionId, string Expansion, string Place, string Story);

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

    if (quests.Count == 0)
    {
      ImGui.TextDisabled("Your journal has no accepted quests.");
      ImGui.Spacing();
      if (ImGui.Button("Open the Main Scenario index")) MainWindow.OpenWikiSearch("");
      return;
    }

    // Wrapped: at /tmmini's width the line is longer than the window.
    using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled)))
      ImGui.TextWrapped($"Accepted quests ({quests.Count}). Click a quest to open its wiki page.");
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

      foreach (IGrouping<string, Accepted> place in expansion.GroupBy((q) => q.Place))
      {
        if (place.Key.Length > 0) ImGui.TextDisabled($"  {place.Key}");

        using ImRaii.IndentDisposable indent = ImRaii.PushIndent();
        foreach (Accepted quest in place)
        {
          // The id goes in the label so two quests with the same name — the
          // game has a few — stay separate selectables.
          if (ImGui.Selectable($"{quest.Name}##{quest.Id}")) MainWindow.OpenWikiSearch(quest.Name);
          if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open this quest on the FFXIV wiki.");
        }
      }
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
        StoryPage(row)));
    }

    return quests;
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
