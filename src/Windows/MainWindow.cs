namespace TimeMemoria.Windows;

public class MainWindow(Configuration _configuration, IDataService _dataService, IGameGui _gameGui, IDataManager _dataManager, IClassJobProgressService _classJobProgress, ILedgerExportService _ledgerExport, INewsService _newsService, ITocService _tocService, IPacingService _pacing, IPlayerState _playerState, IFestivalService _festivals, IPlaytimeService _playtime, IQuestJournalService _journal) : Window("TimeMemoria##TimeMemoriaMainWindow")
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

      using (ImRaii.TabItemDisposable tabItem = ImRaii.TabItem("News"))
        if (tabItem.Success) DrawNewsTab();

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

    IReadOnlyList<ExpansionProgress> expansions = _dataService.ExpansionProgress;
    IReadOnlyList<CategoryProgress> categories = _dataService.CategoryProgress;

    // Expansion figures already exclude whatever the settings exclude, so these
    // rows always sum to the overall line.
    int overallComplete = expansions.Sum((e) => e.NumComplete);
    int overallTotal = expansions.Sum((e) => e.Total);

    ImGui.TextColored(HeaderColour, "Quest Completion Progress");
    ImGui.Spacing();
    ImGui.Separator();
    ImGui.Spacing();

    DrawProgressRow("Overall", overallComplete, overallTotal);
    ImGui.Spacing();

    foreach (ExpansionProgress expansion in expansions)
      DrawProgressRow(expansion.Name, expansion.NumComplete, expansion.Total);

    if (categories.Count > 0)
    {
      ImGui.Spacing();
      ImGui.Spacing();
      ImGui.TextColored(HeaderColour, "By Category");
      ImGui.Spacing();
      ImGui.Separator();
      ImGui.Spacing();

      // Excluded sections still show their real numbers, greyed, so nothing
      // silently vanishes -- it is just visibly not counted.
      foreach (CategoryProgress category in categories)
        DrawProgressRow(category.Name, category.NumComplete, category.Total, category.Excluded);

      if (categories.Any((c) => c.Excluded))
        ImGui.TextDisabled("  Greyed categories are shown but not counted in the totals above.");
    }

    ImGui.Spacing();
    ImGui.Spacing();
    ImGui.Separator();
    ImGui.Spacing();

    DrawSuggestedQuest();
  }

  /// <summary>One aligned "name  complete/total  percent" line.</summary>
  private static void DrawProgressRow(string label, int complete, int total, bool dimmed = false)
  {
    float percentX = ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("100%").X - 8f;
    float countX = percentX - ImGui.CalcTextSize("00000/00000").X - 16f;

    string count = $"{complete}/{total}";
    string percent = total > 0 ? $"{(int)(complete / (float)total * 100f)}%" : "—";

    if (dimmed)
    {
      ImGui.TextDisabled(label);
      ImGui.SameLine(countX);
      ImGui.TextDisabled(count);
      ImGui.SameLine(percentX);
      ImGui.TextDisabled(percent);
      return;
    }

    ImGui.Text(label);
    ImGui.SameLine(countX);
    ImGui.Text(count);
    ImGui.SameLine(percentX);
    ImGui.Text(percent);
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
      UnlockState state = _tocService.GetUnlockState(expansion.SortKey);
      if (state != UnlockState.Unlocked)
      {
        DrawLockedExpansion(expansion, state, percentX);
        continue;
      }

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

  /// <summary>
  /// Explains why the tree's percentages can differ from the Overview's. Only
  /// appears when something is actually excluded -- otherwise the two agree and
  /// there is nothing to explain.
  /// </summary>
  private void DrawExclusionNote()
  {
    List<string> excluded = [];
    if (_configuration.ExcludeLevequests) excluded.Add("Levequests");
    if (_configuration.ExcludeOtherQuests) excluded.Add("Other Quests");
    if (excluded.Count == 0) return;

    ImGui.Spacing();
    ImGui.Spacing();
    ImGui.TextDisabled($"{string.Join(" and ", excluded)} {(excluded.Count == 1 ? "is" : "are")} left out of the");
    ImGui.TextDisabled("Overview totals, but shown here in full — so percentages");
    ImGui.TextDisabled("in this tab will not match the Overview.");
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

      // The tree deliberately shows everything, so its percentages will not match
      // the Overview while an exclusion is on. Said once, here, rather than
      // leaving the difference to be noticed and puzzled over.
      DrawExclusionNote();
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

    using ImRaii.TableDisposable table = ImRaii.Table("##questTable", 5,
      ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerV, ImGui.GetContentRegionAvail());
    if (!table.Success) return;

    ImGui.TableSetupScrollFreeze(0, 1);
    ImGui.TableSetupColumn("##check", ImGuiTableColumnFlags.WidthFixed, 22f);
    ImGui.TableSetupColumn("##level", ImGuiTableColumnFlags.WidthFixed, 36f);
    ImGui.TableSetupColumn("Title", ImGuiTableColumnFlags.WidthStretch);
    ImGui.TableSetupColumn("Area", ImGuiTableColumnFlags.WidthFixed, 140f);
    ImGui.TableSetupColumn("Done", ImGuiTableColumnFlags.WidthFixed, 90f);
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

        // Only dates actually observed are shown. Everything finished before the
        // journal started shares one placeholder date, which would be noise.
        ImGui.TableNextColumn();
        if (complete && quest.Ids.Count > 0)
        {
          string? done = _journal.GetCompletionDate(quest.Ids[0]);
          if (done is not null && !_journal.IsPriorToTracking(done))
            ImGui.TextDisabled(done);
        }
      }

      // Genre headers need the extra cell now that the table has five columns.
      _ = 0;
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
      _dataService.UpdateQuestData();
    }

    bool excludeLevequests = _configuration.ExcludeLevequests;
    if (ImGui.Checkbox("Exclude \'Levequests\' from Overall", ref excludeLevequests))
    {
      _configuration.ExcludeLevequests = excludeLevequests;
      _configuration.Save();
      _dataService.UpdateQuestData();
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

  /// <summary>Role colours, following the game's own tank/healer/DPS convention.</summary>
  private static readonly Dictionary<string, Vector4> RoleColours = new()
  {
    ["Tank"] = new(0.34f, 0.58f, 0.92f, 1.0f),
    ["Healer"] = new(0.35f, 0.78f, 0.47f, 1.0f),
    ["DPS"] = new(0.85f, 0.40f, 0.38f, 1.0f),
    ["Crafter"] = new(0.72f, 0.58f, 0.88f, 1.0f),
    ["Gatherer"] = new(0.88f, 0.72f, 0.38f, 1.0f)
  };

  private string _copyFeedback = "";
  private DateTime _copyShownAt = DateTime.MinValue;

  private void DrawProgressionTab()
  {
    using ImRaii.ChildDisposable child = ImRaii.Child("##progressionTab", ImGuiHelpers.ScaledVector2(0), true);
    if (!child.Success) return;

    List<ClassJobProgress> progress = _classJobProgress.GetProgress();
    List<ClassJobProgress> unlocked = [.. progress.Where((p) => p.IsUnlocked)];

    if (unlocked.Count == 0)
    {
      ImGui.TextDisabled("No character loaded.");
      return;
    }

    // The least progressed job in each role. Compared on level plus the fraction
    // through it -- the same figure the ledger encodes -- because two jobs at the
    // same level are not equally far along, and the one with less experience is
    // the one actually worth levelling.
    Dictionary<string, float> lowestByRole = unlocked
      .GroupBy((p) => p.Role)
      .ToDictionary((g) => g.Key, (g) => g.Min(Effective));

    ImGui.TextColored(HeaderColour, "Class & Job Progression");
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
    ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 165f);
    ImGui.TableSetupColumn("Level", ImGuiTableColumnFlags.WidthFixed, 50f);
    ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthStretch);
    ImGui.TableSetupColumn("EXP", ImGuiTableColumnFlags.WidthFixed, 170f);
    ImGui.TableHeadersRow();

    foreach (ClassJobProgress job in unlocked)
    {
      bool isLowest = lowestByRole.TryGetValue(job.Role, out float lowest)
                      && Math.Abs(Effective(job) - lowest) < 0.0005f;

      ImGui.TableNextRow();

      ImGui.TableNextColumn();
      Vector4 colour = RoleColours.GetValueOrDefault(job.Role, new Vector4(1f, 1f, 1f, 1f));
      ImGui.TextColored(colour, isLowest ? $"▸ {job.Name}" : $"   {job.Name}");
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip(isLowest ? $"{job.Role} — lowest of your {job.Role.ToLowerInvariant()}s" : job.Role);

      ImGui.TableNextColumn();
      ImGui.TextUnformatted(job.Level.ToString());

      ImGui.TableNextColumn();
      if (job.IsMaxLevel) ImGui.TextDisabled("Max level");
      else ImGui.ProgressBar(job.Fraction, new Vector2(-1f, 0f), string.Empty);

      ImGui.TableNextColumn();
      ImGui.TextDisabled(job.IsMaxLevel ? "—" : $"{job.Experience:N0} / {job.ExperienceToNext:N0}");
    }
  }

  /// <summary>Level plus progress through it, rounded the way the export rounds it.</summary>
  private static float Effective(ClassJobProgress job)
    => job.Level + (float)Math.Round(job.Fraction, 3, MidpointRounding.AwayFromZero);

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

  private void DrawNewsTab()
  {
    using ImRaii.ChildDisposable child = ImRaii.Child("##newsTab", ImGuiHelpers.ScaledVector2(0), true);
    if (!child.Success) return;

    _newsService.Poll();

    DrawCharacterSection();
    ImGui.Spacing();
    ImGui.Spacing();
    DrawPacingSection();
    ImGui.Spacing();
    ImGui.Spacing();

    NewsEvent? data = _newsService.Latest;
    if (data == null)
    {
      ImGui.TextDisabled(_newsService.IsLoading ? "Loading world state..." : "No world state available.");
      if (_newsService.FetchError != null)
      {
        ImGui.Spacing();
        ImGui.TextDisabled($"Last error: {_newsService.FetchError}");
      }
      return;
    }

    DrawMaintenanceSection(data);

    // Active Events is hidden for now. The feed's event list is empty because
    // its keyword filter drops anything outside eleven known seasonal names,
    // and the festivals read from the client have no names to show -- the
    // Festival sheet's Name column is empty for all 264 rows. Restore the call
    // once the feed is producing events again.
    // DrawEventsSection(data);
  }

  private static void DrawMaintenanceSection(NewsEvent data)
  {
    ImGui.TextColored(HeaderColour, "Maintenance");
    ImGui.Separator();
    ImGui.Spacing();

    if (data.Maintenance == null)
    {
      ImGui.TextDisabled("No upcoming maintenance.");
    }
    else
    {
      MaintenanceWindow m = data.Maintenance;
      long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

      bool upcoming = m.Start.HasValue && m.Start.Value > now;
      bool serversDown = m.Start.HasValue && m.Start.Value <= now && m.End.HasValue && m.End.Value > now;

      // Three states, because the useful number differs in each: how long until
      // it starts, how long until servers return, or nothing once it is over.
      if (serversDown)
      {
        ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.4f, 1.0f), $"[Servers down]  {m.Title ?? "Maintenance"}");
        ImGui.TextDisabled($"  Back in {FormatSpan(TimeSpan.FromSeconds(m.End!.Value - now))}");
      }
      else if (upcoming)
      {
        ImGui.Text($"[Upcoming]  {m.Title ?? "Maintenance"}");
        ImGui.TextDisabled($"  Starts in {FormatSpan(TimeSpan.FromSeconds(m.Start!.Value - now))}");
      }
      else
      {
        ImGui.Text($"[Completed]  {m.Title ?? "Maintenance"}");
      }

      if (m.Start.HasValue) ImGui.TextDisabled($"  Starts: {FormatUnixLocal(m.Start.Value)}");
      if (m.End.HasValue) ImGui.TextDisabled($"  Ends:   {FormatUnixLocal(m.End.Value)}");
      DrawLink(m.Url);
    }

    ImGui.Spacing();

    if (data.LastMaintenance != null)
    {
      ImGui.TextDisabled($"Last:  {data.LastMaintenance.Title ?? "Maintenance"}");
      if (data.LastMaintenance.End.HasValue)
        ImGui.TextDisabled($"  Ended: {FormatUnixLocal(data.LastMaintenance.End.Value)}");
      DrawLink(data.LastMaintenance.Url);
    }
  }

  /// <summary>
  /// Events from the feed, plus anything the client says is running that the
  /// feed does not carry. The feed only recognises events whose titles match a
  /// known list, so collaborations and one-offs fall through it; the client has
  /// no such blind spot, but also has no end dates.
  /// </summary>
  private void DrawEventsSection(NewsEvent data)
  {
    ImGui.TextColored(HeaderColour, "Active Events");
    ImGui.Separator();
    ImGui.Spacing();

    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    List<string> shown = [];
    bool any = false;

    foreach (GameEvent ev in data.Events)
    {
      bool active = ev.Start.HasValue && ev.Start.Value <= now && ev.End.HasValue && ev.End.Value > now;
      bool upcoming = ev.Start.HasValue && ev.Start.Value > now;
      if (!active && !upcoming) continue;

      any = true;
      if (ev.Title is not null) shown.Add(ev.Title);

      ImGui.Text($"{(active ? "[Active]" : "[Upcoming]")}  {ev.Title ?? "Event"}");

      if (active && ev.End.HasValue)
        ImGui.TextDisabled($"  Ends in {FormatSpan(TimeSpan.FromSeconds(ev.End.Value - now))}");
      else if (upcoming && ev.Start.HasValue)
        ImGui.TextDisabled($"  Starts in {FormatSpan(TimeSpan.FromSeconds(ev.Start.Value - now))}");

      DrawLink(ev.Url);
      ImGui.Spacing();
    }

    // Anything the client has switched on that the feed did not mention.
    List<ActiveFestival> missing = [.. _festivals.GetActive()
      .Where((f) => !shown.Any((t) => Overlaps(t, f.DisplayName)))];

    foreach (ActiveFestival festival in missing)
    {
      any = true;
      ImGui.Text($"[Active]  {festival.DisplayName}");
      ImGui.TextDisabled("  Running now — end date not published to the feed.");
      ImGui.Spacing();
    }

    if (!any) ImGui.TextDisabled("No active or upcoming events.");

    if (missing.Count > 0)
    {
      ImGui.Spacing();
      ImGui.TextDisabled($"  {missing.Count} event{(missing.Count == 1 ? " is" : "s are")} live in game but absent from the news feed.");
      if (ImGui.IsItemHovered())
        ImGui.SetTooltip("The feed only recognises events whose titles match a known list.\nThese were read from the game instead.");
    }
  }

  /// <summary>Loose title match, since feed titles are prose and sheet names are short.</summary>
  private static bool Overlaps(string feedTitle, string festivalName)
  {
    if (festivalName.Length < 4) return false;
    return feedTitle.Contains(festivalName, StringComparison.OrdinalIgnoreCase)
        || festivalName.Contains(feedTitle, StringComparison.OrdinalIgnoreCase);
  }

  private static void DrawLink(string? url)
  {
    if (string.IsNullOrWhiteSpace(url)) return;

    ImGui.SameLine();
    ImGui.TextDisabled("[read]");
    if (ImGui.IsItemHovered())
    {
      ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
      ImGui.SetTooltip(url);
    }
    if (ImGui.IsItemClicked()) Dalamud.Utility.Util.OpenLink(url);
  }

  /// <summary>Local time with the year, so a stale feed is obvious rather than ambiguous.</summary>
  private static string FormatUnixLocal(long unixSeconds) =>
    DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("MMM d, yyyy, h:mm tt");

  private static string FormatSpan(TimeSpan span)
  {
    if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
    if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
    if (span.TotalMinutes >= 1) return $"{(int)span.TotalMinutes}m";
    return "less than a minute";
  }

  /// <summary>
  /// A locked expansion still occupies a row, so the shape of the story stays
  /// visible, but neither its name nor its counts give anything away.
  /// </summary>
  private static void DrawLockedExpansion(QuestData expansion, UnlockState state, float percentX)
  {
    using (ImRaii.ColorDisposable colour = ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
      ImGui.TreeNodeEx($"{expansion.Title}##locked_{expansion.Title}",
        ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen | ImGuiTreeNodeFlags.SpanAvailWidth);

    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip(state == UnlockState.FreeTrialLocked
        ? "Requires the full version of FINAL FANTASY XIV.\nFree Trial Mode is on in Settings."
        : "You have not reached this expansion yet.\nTurn on Spoiler Mode in Settings to browse it anyway.");
    }

    ImGui.SameLine(percentX);
    ImGui.TextDisabled(state == UnlockState.FreeTrialLocked ? "trial" : "—");
  }

  private void DrawSpoilerSettings()
  {
    ImGui.Spacing();
    ImGui.TextColored(HeaderColour, "Story Visibility");
    ImGui.Separator();
    ImGui.Spacing();

    bool spoilerMode = _configuration.SpoilerMode;
    if (ImGui.Checkbox("Spoiler Mode (show expansions you have not reached)", ref spoilerMode))
    {
      _configuration.SpoilerMode = spoilerMode;
      _configuration.Save();
      ResetSelections(true);
    }

    ImGui.Spacing();

    bool freeTrialMode = _configuration.FreeTrialMode;
    if (ImGui.Checkbox("Free Trial Mode (restrict to Stormblood and earlier)", ref freeTrialMode))
    {
      _configuration.FreeTrialMode = freeTrialMode;
      _configuration.Save();
      ResetSelections(true);
    }

    ImGui.TextDisabled(_tocService.IsTrialAccount
      ? "  This account looks like a free trial account."
      : "  This account owns content beyond the free trial.");
    ImGui.TextDisabled("  Free Trial Mode cannot be overridden by Spoiler Mode.");
  }

  /// <summary>
  /// Who this character is, read straight from player state. The previous plugin
  /// worked some of this out by testing whether particular quest ids were
  /// complete, which broke on anything unusual.
  /// </summary>
  private unsafe void DrawCharacterSection()
  {
    ImGui.TextColored(HeaderColour, "Character Information");
    ImGui.Separator();
    ImGui.Spacing();

    if (!_playerState.IsLoaded)
    {
      ImGui.TextDisabled("  No character loaded.");
      return;
    }

    FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState* state =
      FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();

    // PlayerState documents Sex as 0 = male, 1 = female.
    bool feminine = state is not null && state->Sex == 1;

    string race = Pick(_playerState.Race.ValueNullable?.Masculine.ToString(),
                       _playerState.Race.ValueNullable?.Feminine.ToString(), feminine);
    string tribe = Pick(_playerState.Tribe.ValueNullable?.Masculine.ToString(),
                        _playerState.Tribe.ValueNullable?.Feminine.ToString(), feminine);

    DrawLabelled("Race:", race.Length > 0 && tribe.Length > 0 ? $"{race} — {tribe}" : Name(race));
    DrawLabelled("Guardian:", Name(_playerState.GuardianDeity.ValueNullable?.Name.ToString()));
    DrawLabelled("Nameday:", Nameday(_playerState.BirthMonth, _playerState.BirthDay));

    ImGui.Spacing();

    DrawLabelled("Starting City:", Name(_playerState.StartTown.ValueNullable?.Name.ToString()));
    DrawLabelled("Starting Class:", Name(_playerState.FirstClass.ValueNullable?.Name.ToString()));

    string company = Name(_playerState.GrandCompany.ValueNullable?.Name.ToString(), "None");
    if (state is not null && _playerState.GrandCompany.RowId != 0)
    {
      byte rank = _playerState.GetGrandCompanyRank(_playerState.GrandCompany.ValueNullable!.Value);
      if (rank > 0) company += $"  (rank {rank})";
    }
    DrawLabelled("Grand Company:", company);

    // Two flags worth keeping: one marks a 1.0 veteran, the other a returning
    // player's mentor. Neither exists anywhere else in the plugin.
    List<string> marks = [];
    if (state is not null && state->IsLegacy) marks.Add("Legacy");
    if (state is not null && state->IsWarriorOfLight) marks.Add("Warrior of Light");
    if (marks.Count > 0) DrawLabelled("Standing:", string.Join(", ", marks));

    ImGui.Spacing();
    ImGui.TextColored(HeaderColour, "Social");
    ImGui.Separator();
    ImGui.Spacing();

    DrawLabelled("Commendations:", _playerState.PlayerCommendations.ToString());
    DrawLabelled("Custom Deliveries:", $"rank {_playerState.DeliveryLevel}");

    List<string> standing = [];
    if (_playerState.IsBattleMentor) standing.Add("Battle Mentor");
    if (_playerState.IsTradeMentor) standing.Add("Trade Mentor");
    if (_playerState.IsMentor && standing.Count == 0) standing.Add("Mentor");
    if (_playerState.IsNovice) standing.Add("Novice");
    if (_playerState.IsReturner) standing.Add("Returner");

    DrawLabelled("Status:", standing.Count > 0 ? string.Join(", ", standing) : "—");

    ImGui.Spacing();
    DrawPlaytimeLine();
  }

  /// <summary>
  /// Total playtime with the moment it was captured, in local time. The game
  /// only reveals this through the /playtime response, so the figure is exactly
  /// as old as the last time the player ran it -- which makes the timestamp part
  /// of the reading rather than decoration.
  /// </summary>
  private void DrawPlaytimeLine()
  {
    PlaytimeRecord? record = _playtime.Current;

    if (record is null || record.LifetimePlaytime <= TimeSpan.Zero)
    {
      DrawLabelled("Playtime:", "—");
      ImGui.TextDisabled("    Run /playtime once to record it.");
      return;
    }

    TimeSpan total = record.LifetimePlaytime;
    DrawLabelled("Playtime:", $"{(int)total.TotalDays}d {total.Hours}h {total.Minutes}m");

    if (!record.LifetimePlaytimeUpdatedUtc.HasValue)
    {
      ImGui.TextDisabled("    Recorded before this version; age unknown.");
      return;
    }

    DateTime recorded = record.LifetimePlaytimeUpdatedUtc.Value.ToLocalTime();
    ImGui.TextDisabled($"    as of {recorded:MMM d, yyyy, h:mm tt} ({Ago(DateTime.UtcNow - record.LifetimePlaytimeUpdatedUtc.Value)})");
  }

  private static string Ago(TimeSpan span)
  {
    if (span < TimeSpan.Zero) return "just now";
    if (span.TotalMinutes < 1) return "just now";
    if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
    if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
    return $"{(int)span.TotalDays}d ago";
  }

  private static string Pick(string? masculine, string? feminine, bool useFeminine)
  {
    string chosen = useFeminine ? feminine ?? "" : masculine ?? "";
    if (chosen.Length == 0) chosen = masculine ?? feminine ?? "";
    return ClassJobProgressService.ToDisplayName(chosen);
  }

  /// <summary>
  /// Eorzean namedays alternate Astral and Umbral moons, so month 1 is the 1st
  /// Astral Moon, month 2 the 1st Umbral, and so on.
  /// </summary>
  private static string Nameday(byte month, byte day)
  {
    if (month is 0 or > 12 || day is 0 or > 31) return "—";

    string moon = month % 2 == 1 ? "Astral" : "Umbral";
    int index = (month + 1) / 2;
    return $"{Ordinal(day)} Sun of the {Ordinal(index)} {moon} Moon";
  }

  private static string Ordinal(int n)
  {
    string suffix = (n % 100) is >= 11 and <= 13
      ? "th"
      : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
    return $"{n}{suffix}";
  }

  /// <summary>
  /// Session pacing is a delta: quests finished since login over time played
  /// since login. Overall is the character's whole history. Both are
  /// observational -- no thresholds, no judgement.
  /// </summary>
  private void DrawPacingSection()
  {
    ImGui.TextColored(HeaderColour, "Quest Pacing");
    ImGui.Separator();
    ImGui.Spacing();

    double? session = _pacing.SessionMinutesPerQuest;
    DrawLabelled("Session pacing:", session.HasValue ? PacingService.Format(session.Value) : "—");

    if (_pacing.SessionQuests > 0)
      ImGui.TextDisabled($"    {_pacing.SessionQuests} quest{(_pacing.SessionQuests == 1 ? "" : "s")} this session");

    double? overall = _pacing.OverallMinutesPerQuest;
    DrawLabelled("Overall pacing:", overall.HasValue ? PacingService.Format(overall.Value) : "—");

    if (!_pacing.HasLifetimePlaytime)
      ImGui.TextDisabled("    Run /playtime once to enable overall pacing.");
    else
      ImGui.TextDisabled($"    across {_pacing.TotalComplete} completed quests");

    ImGui.Spacing();
    ImGui.Spacing();
    DrawStoryEstimates();
  }

  /// <summary>
  /// How much Main Scenario is left, and what that has historically cost in
  /// hours. Descriptive only -- it reports the rate this character has actually
  /// managed, and makes no claim about how fast anyone ought to be.
  /// </summary>
  private void DrawStoryEstimates()
  {
    ImGui.TextColored(HeaderColour, "Story Remaining");
    ImGui.Separator();
    ImGui.Spacing();

    IReadOnlyList<ExpansionProgress> msq = _dataService.MsqProgress;
    List<ExpansionProgress> remaining = [.. msq.Where((e) => e.NumComplete < e.Total)];

    if (remaining.Count == 0)
    {
      ImGui.TextDisabled("  Every Main Scenario quest is complete.");
      return;
    }

    double? rate = _pacing.MsqMinutesPerQuest;

    // The expansion currently in progress is the gate to the next one, so its
    // remaining count doubles as the countdown to whatever comes after.
    ExpansionProgress current = remaining[0];
    int toGate = current.Total - current.NumComplete;
    ExpansionProgress? next = msq.FirstOrDefault((e) => e.Id == current.Id + 1);

    if (next is not null)
      ImGui.TextDisabled($"  {toGate} Main Scenario quest{(toGate == 1 ? "" : "s")} until {next.Name} opens.");

    ImGui.Spacing();

    foreach (ExpansionProgress expansion in remaining)
    {
      int left = expansion.Total - expansion.NumComplete;
      string estimate = rate.HasValue ? $"~{FormatHours(left * rate.Value)}" : "—";

      ImGui.TextDisabled($"  {expansion.Name}");
      ImGui.SameLine(170.0f);
      ImGui.Text($"{left} left");
      ImGui.SameLine(250.0f);
      ImGui.TextDisabled(estimate);
    }

    ImGui.Spacing();

    if (rate.HasValue)
    {
      int totalLeft = remaining.Sum((e) => e.Total - e.NumComplete);
      ImGui.TextDisabled($"  {totalLeft} remaining at your rate of {PacingService.Format(rate.Value)}");
      ImGui.TextDisabled($"  — roughly {FormatHours(totalLeft * rate.Value)} of play.");
    }
    else
    {
      ImGui.TextDisabled("  Run /playtime once to enable estimates.");
    }
  }

  private static string FormatHours(double minutes)
  {
    if (minutes < 60) return $"{(int)minutes}m";
    double hours = minutes / 60.0;
    if (hours < 24) return $"{hours:F1}h";
    return $"{hours / 24.0:F1} days";
  }

  /// <summary>
  /// Sheet names arrive lowercase ("marauder") while the game presents them
  /// capitalised, so they are re-cased for display.
  /// </summary>
  private static string Name(string? raw, string fallback = "Unknown")
    => string.IsNullOrWhiteSpace(raw) ? fallback : ClassJobProgressService.ToDisplayName(raw);

  private static void DrawLabelled(string label, string value)
  {
    ImGui.TextDisabled($"  {label}");
    ImGui.SameLine(150.0f);
    ImGui.Text(value);
  }
}
