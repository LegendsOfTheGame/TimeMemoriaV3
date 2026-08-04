namespace TimeMemoria.Windows;

public class MainWindow(Configuration _configuration, IDataService _dataService, IGameGui _gameGui, IDataManager _dataManager, IClassJobProgressService _classJobProgress, ILedgerExportService _ledgerExport) : Window("TimeMemoria##TimeMemoriaMainWindow")
{
  private static readonly Vector4 HeaderColour = new(0.5f, 0.8f, 1.0f, 1.0f);

  private string _searchQuery = "";

  private QuestData? _selectedCategory;
  private string _selectedLabel = "";
  private float _leftPanelWidth = 282f;

  public override void Draw()
  {
    _dataService.UpdateQuestData();

    Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    ImGui.SetNextWindowSize(new Vector2(475, 375), ImGuiCond.FirstUseEver);
    SizeConstraints = new()
    {
      MinimumSize = new Vector2(475, 240),
      MaximumSize = new Vector2(float.MaxValue)
    };

    using (ImRaii.TabBarDisposable tabBar = ImRaii.TabBar("##tabBar", ImGuiTabBarFlags.None))
    {
      if (!tabBar.Success) return;

      using (ImRaii.TabItemDisposable tabItem = ImRaii.TabItem("Overview"))
        if (tabItem.Success) DrawOverviewTab();

      using (ImRaii.TabItemDisposable tabItem = ImRaii.TabItem("Quests"))
        if (tabItem.Success) DrawQuestsTab();

      using (ImRaii.TabItemDisposable tabItem = ImRaii.TabItem("Progression"))
        if (tabItem.Success) DrawProgressionTab();

      using (ImRaii.TabItemDisposable tabItem = ImRaii.TabItem("Settings"))
        if (tabItem.Success) DrawSettingsTab();
    }
  }

  private void DrawOverviewTab()
  {
    using ImRaii.ChildDisposable child = ImRaii.Child("##overviewTab", ImGuiHelpers.ScaledVector2(0), true);
    if (!child.Success) return;

    ImGui.TextColored(HeaderColour, "Quest Completion Progress");
    ImGui.Spacing();
    ImGui.Separator();
    ImGui.Spacing();

    IReadOnlyList<ExpansionProgress> expansions = _dataService.ExpansionProgress;

    int overallComplete = expansions.Sum((e) => e.NumComplete);
    int overallTotal = expansions.Sum((e) => e.Total);

    DrawProgressRow("Overall", overallComplete, overallTotal);
    ImGui.Spacing();

    foreach (ExpansionProgress expansion in expansions)
      DrawProgressRow(expansion.Name, expansion.NumComplete, expansion.Total);

    ImGui.Spacing();
    ImGui.Spacing();
    ImGui.Separator();
    ImGui.Spacing();

    DrawSuggestedQuest();
  }

  /// <summary>One aligned "name  complete/total  percent" line.</summary>
  private static void DrawProgressRow(string label, int complete, int total)
  {
    float percentX = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("100%").X - 8f;
    float countX = percentX - ImGui.CalcTextSize("00000/00000").X - 16f;

    ImGui.Text(label);
    ImGui.SameLine(countX);
    ImGui.Text($"{complete}/{total}");
    ImGui.SameLine(percentX);
    ImGui.Text(total > 0 ? $"{(int)(complete / (float)total * 100f)}%" : "—");
  }

  /// <summary>
  /// The next unfinished quest in release order, with where it sits and where it
  /// starts, so the answer to "what now" is one glance rather than a search.
  /// </summary>
  private void DrawSuggestedQuest()
  {
    ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.5f, 1.0f), "Suggested Quest");
    ImGui.Spacing();

    Types.Quest? quest = _dataService.FindOldestIncomplete(out string expansion, out string category);
    if (quest == null)
    {
      ImGui.TextDisabled("  Nothing left — every tracked quest is complete.");
      return;
    }

    if (expansion.Length > 0) ImGui.TextDisabled($"  {expansion}");
    if (category.Length > 0) ImGui.TextDisabled($"    {category}");

    ImGui.Text($"      {quest.Title}");
    if (quest.Ids.Count > 0)
    {
      ImGui.SameLine();
      ImGui.TextDisabled($"[{quest.Ids[0]}]");
    }

    string where = quest.Area.Length > 0 ? $"Level {quest.Level} • {quest.Area}" : $"Level {quest.Level}";
    ImGui.TextDisabled($"      {where}");
  }

  private void DrawQuestsTab()
  {
    float totalWidth = ImGui.GetContentRegionAvail().X;
    float splitterWidth = 4f;
    float leftWidth = Math.Clamp(_leftPanelWidth, 200f, Math.Max(220f, totalWidth - 220f));
    float rightWidth = totalWidth - leftWidth - splitterWidth - (ImGui.GetStyle().ItemSpacing.X * 2);
    float panelHeight = ImGui.GetContentRegionAvail().Y;

    DrawQuestTree(leftWidth, panelHeight);
    DrawSplitter(splitterWidth, panelHeight, totalWidth);
    DrawQuestList(rightWidth, panelHeight);
  }

  /// <summary>
  /// Left panel: expansion, then journal section, then category. Percentages sit
  /// on a fixed column so they line up regardless of nesting depth.
  /// </summary>
  private void DrawQuestTree(float width, float height)
  {
    using ImRaii.ChildDisposable child = ImRaii.Child("##questTree", new Vector2(width, height), true);
    if (!child.Success) return;

    float percentX = ImGui.GetWindowWidth()
                     - ImGui.CalcTextSize("100%").X
                     - ImGui.GetStyle().WindowPadding.X
                     - ImGui.GetStyle().ScrollbarSize;

    foreach (QuestData expansion in _dataService.QuestData.Categories)
    {
      bool expansionOpen = ImGui.TreeNodeEx($"{expansion.Title}##exp_{expansion.Title}", ImGuiTreeNodeFlags.SpanAvailWidth);
      DrawPercentAt(percentX, expansion.NumComplete, expansion.Total);
      if (!expansionOpen) continue;

      foreach (QuestData section in expansion.Categories)
      {
        bool sectionOpen = ImGui.TreeNodeEx($"{section.Title}##sec_{expansion.Title}_{section.Title}", ImGuiTreeNodeFlags.SpanAvailWidth);
        DrawPercentAt(percentX, section.NumComplete, section.Total);
        if (!sectionOpen) continue;

        foreach (QuestData category in section.Categories)
        {
          bool selected = ReferenceEquals(_selectedCategory, category);

          if (ImGui.Selectable($"  {category.Title}##cat_{expansion.Title}_{section.Title}_{category.Title}",
                               selected, ImGuiSelectableFlags.None,
                               new Vector2(Math.Max(percentX - ImGui.GetCursorPosX() - 4f, 1f), 0)))
          {
            _selectedCategory = category;
            _selectedLabel = $"{expansion.Title} — {section.Title} — {category.Title}";
          }

          DrawPercentAt(percentX, category.NumComplete, category.Total);
        }

        ImGui.TreePop();
      }

      ImGui.TreePop();
    }
  }

  private static void DrawPercentAt(float x, float complete, float total)
  {
    ImGui.SameLine(x);
    ImGui.TextDisabled(total > 0 ? $"{(int)(complete / total * 100f)}%" : "—");
  }

  /// <summary>Draggable divider between the tree and the quest list.</summary>
  private void DrawSplitter(float width, float height, float totalWidth)
  {
    ImGui.SameLine();
    Vector2 pos = ImGui.GetCursorScreenPos();
    ImGui.InvisibleButton("##splitter", new Vector2(width, height));

    bool hovered = ImGui.IsItemHovered();
    bool active = ImGui.IsItemActive();
    if (hovered || active) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
    if (active)
      _leftPanelWidth = Math.Clamp(_leftPanelWidth + ImGui.GetIO().MouseDelta.X, 200f, Math.Max(220f, totalWidth - 220f));

    uint colour = active ? ImGui.GetColorU32(ImGuiCol.SeparatorActive)
                  : hovered ? ImGui.GetColorU32(ImGuiCol.SeparatorHovered)
                  : ImGui.GetColorU32(ImGuiCol.Separator);

    float midX = pos.X + (width / 2f);
    ImGui.GetWindowDrawList().AddLine(new Vector2(midX, pos.Y), new Vector2(midX, pos.Y + height), colour, 1f);
  }

  /// <summary>
  /// Right panel: the selected category's quests, grouped under their journal
  /// genre or place name, which is what gives raid series and sidequest areas
  /// their headers.
  /// </summary>
  private void DrawQuestList(float width, float height)
  {
    ImGui.SameLine();
    using ImRaii.ChildDisposable child = ImRaii.Child("##questList", new Vector2(width, height), true);
    if (!child.Success) return;

    if (_selectedCategory == null)
    {
      float centre = (ImGui.GetContentRegionAvail().Y / 2f) - ImGui.GetTextLineHeight();
      ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Math.Max(centre, 0f));
      ImGui.TextDisabled("Select a category on the left.");
      return;
    }

    float statsWidth = ImGui.CalcTextSize($"{_selectedCategory.NumComplete}/{_selectedCategory.Total} 100%").X;
    ImGui.Text(_selectedLabel);
    ImGui.SameLine(ImGui.GetContentRegionAvail().X - statsWidth);
    ImGui.TextDisabled(_selectedCategory.Total > 0
      ? $"{_selectedCategory.NumComplete}/{_selectedCategory.Total} {(int)(_selectedCategory.NumComplete / _selectedCategory.Total * 100f)}%"
      : "—");
    ImGui.Separator();
    ImGui.Spacing();

    using ImRaii.TableDisposable table = ImRaii.Table("##questTable", 4,
      ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerV, ImGui.GetContentRegionAvail());
    if (!table.Success) return;

    ImGui.TableSetupScrollFreeze(0, 1);
    ImGui.TableSetupColumn("##check", ImGuiTableColumnFlags.WidthFixed, 22f);
    ImGui.TableSetupColumn("##level", ImGuiTableColumnFlags.WidthFixed, 36f);
    ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch);
    ImGui.TableSetupColumn("Area", ImGuiTableColumnFlags.WidthFixed, 140f);
    ImGui.TableHeadersRow();

    foreach (QuestData genre in _selectedCategory.Categories)
    {
      List<Types.Quest> visible = [.. genre.Quests.Where((q) => !q.Hide)];
      if (visible.Count == 0) continue;

      // Genre header, only when the category actually splits into several.
      if (_selectedCategory.Categories.Count > 1)
      {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        ImGui.TableNextColumn();
        ImGui.TextColored(HeaderColour, genre.Title);
        ImGui.TableNextColumn();
      }

      foreach (Types.Quest quest in visible)
      {
        bool complete = _dataService.IsQuestComplete(quest);

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (complete)
        {
          using (ImRaii.FontDisposable font = ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.TextUnformatted(FontAwesomeIcon.Check.ToIconString());
        }

        ImGui.TableNextColumn();
        ImGui.TextDisabled(quest.Level.ToString());

        ImGui.TableNextColumn();
        string title = quest.Ids.Count > 0 ? $"{quest.Title} [{quest.Ids[0]}]" : quest.Title;
        if (complete) ImGui.TextDisabled(title);
        else ImGui.Text(title);

        ImGui.TableNextColumn();
        ImGui.TextDisabled(quest.Area);
      }
    }
  }

  private void DrawSearchResults()
  {
    List<(Types.Quest quest, string categoryPath, QuestData topLevelCategory, QuestData directParentCategory)> allQuests = GetQuests();
    List<(Types.Quest quest, string categoryPath, QuestData topLevelCategory, QuestData directParentCategory)> filteredQuests = allQuests.Where(questWithCategory =>
        questWithCategory.quest.Title.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
        questWithCategory.quest.Area.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)).ToList();

    ImGui.Text($"Search Results ({filteredQuests.Count} found)");
    ImGui.Separator();

    Vector2 availableSize = ImGui.GetContentRegionAvail();
    using ImRaii.ChildDisposable child = ImRaii.Child("##searchResultsScroll", availableSize, false, ImGuiWindowFlags.HorizontalScrollbar);
    if (!child.Success) return;

    using ImRaii.TableDisposable table = ImRaii.Table("##globalQuestTable", 5, ImGuiTableFlags.Resizable |
          ImGuiTableFlags.BordersOuter |
          ImGuiTableFlags.BordersV |
          ImGuiTableFlags.ScrollX |
          ImGuiTableFlags.SizingFixedFit);
    if (!table.Success) return;


    ImGui.TableSetupColumn("##check", ImGuiTableColumnFlags.WidthFixed, 30.0f);
    ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthFixed, 200.0f);
    ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 250.0f);
    ImGui.TableSetupColumn("Area", ImGuiTableColumnFlags.WidthFixed, 180.0f);
    ImGui.TableSetupColumn("Level", ImGuiTableColumnFlags.WidthFixed, 60.0f);
    ImGui.TableHeadersRow();

    foreach ((Types.Quest quest, string categoryPath, QuestData topLevelCategory, QuestData directParentCategory) in filteredQuests)
    {
      if (!quest.Hide)
      {
        ImGui.TableNextColumn();
        if (_dataService.IsQuestComplete(quest))
        {
          ImGui.PushFont(UiBuilder.IconFont);
          ImGui.TextUnformatted(FontAwesomeIcon.Check.ToIconString());
          ImGui.PopFont();
          quest.Hide = _configuration.DisplayOption == 2;
        }

        ImGui.TableNextColumn();
        ImGui.Text(quest.Title);

        ImGui.TableNextColumn();

        // Use the direct parent category title instead of the full path
        if (ImGui.Selectable($"{directParentCategory.Title}##{quest.Ids[0]}_category"))
        {
          NavigateToCategory(topLevelCategory, directParentCategory);
        }

        ImGui.TableNextColumn();
        if (ImGui.Selectable($"{quest.Area}##{quest.Ids[0]}"))
          OpenAreaMap(quest);
        ImGui.TableNextColumn();
        ImGui.Text($"{quest.Level}");
        ImGui.TableNextRow();
      }
    }
  }

  private List<(Types.Quest quest, string categoryPath, QuestData topLevelCategory, QuestData directParentCategory)> GetQuests()
  {
    List<(Types.Quest quest, string categoryPath, QuestData topLevelCategory, QuestData directParentCategory)> allQuests = [];

    foreach (QuestData category in _dataService.QuestData.Categories)
    {
      if (!category.Hide)
      {
        GetQuestsData(category, category, category.Title, allQuests);
      }
    }

    return allQuests;
  }

  private void GetQuestsData(QuestData currentCategory, QuestData topLevelCategory, string categoryPath, List<(Types.Quest quest, string categoryPath, QuestData topLevelCategory, QuestData directParentCategory)> allQuests)
  {
    foreach (Types.Quest quest in currentCategory.Quests)
    {
      allQuests.Add((quest, categoryPath, topLevelCategory, currentCategory));
    }

    foreach (QuestData subCategory in currentCategory.Categories)
    {
      if (!subCategory.Hide)
      {
        string newPath = $"{categoryPath} > {subCategory.Title}";
        GetQuestsData(subCategory, topLevelCategory, newPath, allQuests);
      }
    }
  }

  private void NavigateToCategory(QuestData topLevelCategory, QuestData directParentCategory)
  {
    _searchQuery = "";
    _selectedCategory = directParentCategory == topLevelCategory
      ? topLevelCategory.Categories.Find((c) => !c.Hide)
      : directParentCategory;
    _selectedLabel = _selectedCategory?.Title ?? "";
  }

  private void GetQuests(QuestData questData, string categoryPath, List<(Types.Quest quest, string categoryPath)> allQuests)
  {
    foreach (Types.Quest quest in questData.Quests)
    {
      allQuests.Add((quest, categoryPath));
    }

    foreach (QuestData subCategory in questData.Categories)
    {
      if (!subCategory.Hide)
      {
        string newPath = $"{categoryPath} > {subCategory.Title}";
        GetQuests(subCategory, newPath, allQuests);
      }
    }
  }

  private void DrawQuestTable(List<Types.Quest> quests)
  {
    using ImRaii.TableDisposable table = ImRaii.Table("##questTable", 4, ImGuiTableFlags.SizingStretchSame);
    if (!table.Success) return;

    ImGui.TableSetupColumn("##check", ImGuiTableColumnFlags.None, 0.10f);
    ImGui.TableSetupColumn("Title");
    ImGui.TableSetupColumn("Area", ImGuiTableColumnFlags.None, 0.70f);
    ImGui.TableSetupColumn("Level", ImGuiTableColumnFlags.None, 0.30f);
    ImGui.TableHeadersRow();
    foreach (Types.Quest quest in quests)
    {
      if (!quest.Hide)
      {
        ImGui.TableNextColumn();
        if (_dataService.IsQuestComplete(quest))
        {
          ImGui.PushFont(UiBuilder.IconFont);
          ImGui.TextUnformatted(FontAwesomeIcon.Check.ToIconString());
          ImGui.PopFont();
          quest.Hide = _configuration.DisplayOption == 2;
        }

        ImGui.TableNextColumn();
        ImGui.Text(quest.Title);
        ImGui.TableNextColumn();
        if (ImGui.Selectable($"{quest.Area}##{quest.Ids[0]}")) OpenAreaMap(quest);
        ImGui.TableNextColumn();
        ImGui.Text($"{quest.Level}");
        ImGui.TableNextRow();
      }
    }
  }

  private void DrawSettingsTab()
  {
    using ImRaii.ChildDisposable child = ImRaii.Child("##settingsTab", ImGuiHelpers.ScaledVector2(0), true);
    if (!child.Success) return;

    ImGui.SetNextItemWidth(130 * ImGuiHelpers.GlobalScale);
    int displayOption = _configuration.DisplayOption;
    string[] displayList = ["Show All", "Show Complete", "Show Incomplete"];

    using (ImRaii.ComboDisposable combo = ImRaii.Combo("##displayOption", displayList[displayOption]))
    {
      if (combo.Success)
      {
        for (int i = 0; i < displayList.Length; i++)
        {
          if (ImGui.Selectable(displayList[i]))
          {
            _configuration.DisplayOption = i;
            _configuration.Save();
            _dataService.UpdateQuestData();
            ResetSelections();
          }

          if (displayOption == i) ImGui.SetItemDefaultFocus();
        }
      }
    }

    ImGui.Spacing();

    bool showCount = _configuration.ShowCount;
    if (ImGui.Checkbox("Show count \"Main Scenario 502/843\"", ref showCount))
    {
      _configuration.ShowCount = showCount;
      _configuration.Save();
    }

    ImGui.Spacing();

    bool showPercentage = _configuration.ShowPercentage;
    if (ImGui.Checkbox("Show percentage \"Tribal Quests 32.13%\"", ref showPercentage))
    {
      _configuration.ShowPercentage = showPercentage;
      _configuration.Save();
    }

    ImGui.Spacing();

    bool excludeOtherQuests = _configuration.ExcludeOtherQuests;
    if (ImGui.Checkbox("Exclude \'Other Quests\' from Overall", ref excludeOtherQuests))
    {
      _configuration.ExcludeOtherQuests = excludeOtherQuests;
      _configuration.Save();
    }

    bool excludeLevequests = _configuration.ExcludeLevequests;
    if (ImGui.Checkbox("Exclude \'Levequests\' from Overall", ref excludeLevequests))
    {
      _configuration.ExcludeLevequests = excludeLevequests;
      _configuration.Save();
    }
  }

  public void Reset()
  {
    ResetSelections(true);
  }

  private void ResetSelections(bool force = false)
  {
    if (force || _selectedCategory == null || _selectedCategory.Hide)
    {
      _selectedCategory = null;
      _selectedLabel = "";
    }
  }

  private string GetDisplayText(QuestData? questData)
  {
    if (questData == null) return "";
    string text = $"{questData.Title}";
    if (_configuration.ShowCount) text += $" {questData.NumComplete}/{questData.Total}";
    if (_configuration.ShowPercentage) text += $" {questData.NumComplete / questData.Total:P2}";
    return text;
  }

  private void OpenAreaMap(Types.Quest quest)
  {
    if (quest.IsLeve)
    {
      Level level = _dataManager.GetExcelSheet<Leve>().First(q => quest.Ids.Contains(q.RowId) && q.LevelLevemete.ValueNullable != null).LevelLevemete.Value;
      MapLinkPayload mapLink = new(level.Territory.RowId, level.Map.RowId, (int)(level.X * 1_000f), (int)(level.Z * 1_000f));
      _gameGui.OpenMapWithMapLink(mapLink);
    }
    else
    {
      Level level = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>().First(q => quest.Ids.Contains(q.RowId) && q.IssuerLocation.ValueNullable != null).IssuerLocation.Value;
      MapLinkPayload mapLink = new(level.Territory.RowId, level.Map.RowId, (int)(level.X * 1_000f), (int)(level.Z * 1_000f));
      _gameGui.OpenMapWithMapLink(mapLink);
    }
  }

  private void DrawProgressionTab()
  {
    using ImRaii.ChildDisposable child = ImRaii.Child("##progressionTab", ImGuiHelpers.ScaledVector2(0), true);
    if (!child.Success) return;

    List<ClassJobProgress> progress = _classJobProgress.GetProgress();
    List<ClassJobProgress> unlocked = [.. progress.Where(p => p.IsUnlocked)];

    if (unlocked.Count == 0)
    {
      ImGui.TextDisabled("No character loaded.");
      return;
    }

    ImGui.TextColored(new Vector4(0.5f, 0.8f, 1.0f, 1.0f), "Class & Job Progression");
    ImGui.SameLine();
    ImGui.TextDisabled($"({unlocked.Count} of {progress.Count} unlocked)");
    ImGui.Separator();
    ImGui.Spacing();

    DrawExportRow();
    ImGui.Spacing();

    using ImRaii.TableDisposable table = ImRaii.Table("##progressionTable", 4,
      ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg,
      ImGui.GetContentRegionAvail());
    if (!table.Success) return;

    ImGui.TableSetupScrollFreeze(0, 1);
    ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 150f);
    ImGui.TableSetupColumn("Level", ImGuiTableColumnFlags.WidthFixed, 50f);
    ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthStretch);
    ImGui.TableSetupColumn("EXP", ImGuiTableColumnFlags.WidthFixed, 170f);
    ImGui.TableHeadersRow();

    foreach (ClassJobProgress job in unlocked)
    {
      ImGui.TableNextRow();

      ImGui.TableNextColumn();
      ImGui.TextUnformatted(job.Name);

      ImGui.TableNextColumn();
      ImGui.TextUnformatted(job.Level.ToString());

      ImGui.TableNextColumn();
      if (job.IsMaxLevel) ImGui.TextDisabled("Max level");
      else ImGui.ProgressBar(job.Fraction, new Vector2(-1f, 0f), string.Empty);

      ImGui.TableNextColumn();
      ImGui.TextDisabled(job.IsMaxLevel ? "—" : $"{job.Experience:N0} / {job.ExperienceToNext:N0}");
    }
  }

  private string _copyFeedback = "";
  private DateTime _copyShownAt = DateTime.MinValue;

  private void DrawExportRow()
  {
    if (ImGui.Button("Copy progression to clipboard"))
      CopyToClipboard(_ledgerExport.BuildProgressionJson, "Copied as JSON.");

    ImGui.SameLine();

    if (ImGui.Button("Copy for Adventurer's Ledger"))
      CopyToClipboard(_ledgerExport.BuildLedgerJson, "Copied in ledger format.");

    // Fade the confirmation rather than leaving it on screen forever.
    if (_copyFeedback.Length > 0 && DateTime.UtcNow - _copyShownAt < TimeSpan.FromSeconds(4))
    {
      ImGui.SameLine();
      ImGui.TextDisabled(_copyFeedback);
    }

    ImGui.TextDisabled("Nothing is sent anywhere — the export stays on your clipboard.");
  }

  private void CopyToClipboard(Func<string> build, string successMessage)
  {
    try
    {
      ImGui.SetClipboardText(build());
      _copyFeedback = successMessage;
    }
    catch (Exception ex)
    {
      _copyFeedback = $"Copy failed: {ex.Message}";
    }

    _copyShownAt = DateTime.UtcNow;
  }
}
