namespace TimeMemoria.Windows;

public class MainWindow(Configuration _configuration, IDataService _dataService, IGameGui _gameGui, IDataManager _dataManager, IClassJobProgressService _classJobProgress, ILedgerExportService _ledgerExport) : Window("TimeMemoria##TimeMemoriaMainWindow")
{
  private string _searchQuery = "";

  private QuestData? _categorySelection;
  private QuestData? _subcategorySelection;

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

    using ImRaii.TableDisposable table = ImRaii.Table("##overviewTable", 3, ImGuiTableFlags.SizingStretchSame);
    if (!table.Success) return;

    ImGui.TableSetupColumn("##title");
    ImGui.TableSetupColumn("##count", ImGuiTableColumnFlags.None, 0.70f);
    ImGui.TableSetupColumn("##percentage", ImGuiTableColumnFlags.None, 0.30f);

    float otherQuestsComplete = _dataService.QuestData.Categories.Find((c) => c.Title == _dataService.OtherQuestsTitle)?.NumComplete ?? 0;
    float otherQuestsTotal = _dataService.QuestData.Categories.Find((c) => c.Title == _dataService.OtherQuestsTitle)?.Total ?? 0;

    float levequestsComplete = _dataService.QuestData.Categories.Find((c) => c.Title == _dataService.LevequestsTitle)?.NumComplete ?? 0;
    float levequestsTotal = _dataService.QuestData.Categories.Find((c) => c.Title == _dataService.LevequestsTitle)?.Total ?? 0;

    float overallComplete = _dataService.QuestData.NumComplete
                            - (_configuration.ExcludeOtherQuests ? otherQuestsComplete : 0f)
                            - (_configuration.ExcludeLevequests ? levequestsComplete : 0f);

    float overallTotal = _dataService.QuestData.Total
                        - (_configuration.ExcludeOtherQuests ? otherQuestsTotal : 0f)
                        - (_configuration.ExcludeLevequests ? levequestsTotal : 0f);

    ImGui.TableNextColumn();
    ImGui.Text("Overall");
    ImGui.Separator();
    ImGui.TableNextColumn();
    ImGui.Text($"{overallComplete}/{overallTotal}");
    ImGui.Separator();
    ImGui.TableNextColumn();
    ImGui.Text($"{overallComplete / overallTotal:P2}");
    ImGui.Separator();
    ImGui.TableNextRow();

    foreach (QuestData category in _dataService.QuestData.Categories)
    {
      if ((category.Title == _dataService.LevequestsTitle && _configuration.ExcludeLevequests) || (category.Title == _dataService.OtherQuestsTitle && _configuration.ExcludeOtherQuests))
      {
        ImGui.TableNextColumn();
        ImGui.TextDisabled(category.Title);
        ImGui.TableNextColumn();
        ImGui.TextDisabled($"{category.NumComplete}/{category.Total}");
        ImGui.TableNextColumn();
        ImGui.TextDisabled($"{category.NumComplete / category.Total:P2}");
        ImGui.TableNextRow();
      }
      else
      {
        ImGui.TableNextColumn();
        ImGui.Text(category.Title);
        ImGui.TableNextColumn();
        ImGui.Text($"{category.NumComplete}/{category.Total}");
        ImGui.TableNextColumn();
        ImGui.Text($"{category.NumComplete / category.Total:P2}");
        ImGui.TableNextRow();
      }
    }
  }

  private void DrawQuestsTab()
  {
    using ImRaii.ChildDisposable child = ImRaii.Child("##questsTab", ImGuiHelpers.ScaledVector2(0), true);
    if (!child.Success) return;

    if (_categorySelection == null) ResetSelections();

    // If there's search text, show search results (this is global, not category-specific)
    if (!string.IsNullOrWhiteSpace(_searchQuery))
    {
      ImGui.SetNextItemWidth(-1);
      ImGui.InputTextWithHint("##search_input", "Search all quests...", ref _searchQuery, 256);
      ImGui.Spacing();

      DrawSearchResults();
    }
    else
    {
      float availableWidth = ImGui.GetContentRegionAvail().X;
      float searchBoxWidth = 400;
      float spacing = ImGui.GetStyle().ItemSpacing.X;
      float comboWidth = 400;

      ImGui.SetNextItemWidth(comboWidth);
      using (ImRaii.ComboDisposable combo = ImRaii.Combo("##categoryDropdown", GetDisplayText(_categorySelection)))
      {
        if (combo.Success)
        {
          foreach (QuestData category in _dataService.QuestData.Categories)
          {
            if (!category.Hide)
            {
              if (ImGui.Selectable(GetDisplayText(category), _categorySelection == category))
              {
                _categorySelection = category;
                _subcategorySelection =
                    _categorySelection.Categories.Find(c => !c.Hide);
              }
            }
          }
        }
      }

      ImGui.SameLine();
      ImGui.SetNextItemWidth(availableWidth - searchBoxWidth);
      ImGui.InputTextWithHint("##search_input", "Search all quests...", ref _searchQuery, 256);

      ImGui.Spacing();
      ImGui.SetNextItemWidth(comboWidth);

      using (ImRaii.ComboDisposable combo = ImRaii.Combo("##subcategoryDropdown", GetDisplayText(_subcategorySelection)))
      {
        if (combo.Success)
        {
          foreach (QuestData category in _categorySelection?.Categories ?? [])
          {
            if (!category.Hide)
            {
              if (ImGui.Selectable(GetDisplayText(category), _subcategorySelection == category))
                _subcategorySelection = category;

              if (_subcategorySelection == category) ImGui.SetItemDefaultFocus();
            }
          }
        }
      }

      ImGui.Spacing();
      if (_subcategorySelection?.Categories.Count > 0)
      {
        foreach (QuestData subcategory in _subcategorySelection.Categories)
        {
          if (!subcategory.Hide)
          {
            ImGui.TextDisabled($"{subcategory.Title}");
            ImGui.Separator();
            DrawQuestTable(subcategory.Quests);
          }
        }
      }
      else
      {
        DrawQuestTable(_subcategorySelection?.Quests ?? []);
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

    _categorySelection = topLevelCategory;

    if (directParentCategory == topLevelCategory)
    {
      _subcategorySelection = topLevelCategory.Categories.Find(c => !c.Hide);
    }
    else
    {
      _subcategorySelection = directParentCategory;
    }
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
    if (_categorySelection == null || _categorySelection.Hide || force)
    {
      _categorySelection = _dataService.QuestData.Categories.Find(c => !c.Hide);
      _subcategorySelection = _categorySelection?.Categories.Find(c => !c.Hide);
    }

    if (_subcategorySelection == null || _subcategorySelection.Hide || force)
    {
      _subcategorySelection = _categorySelection?.Categories.Find(c => !c.Hide);
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
