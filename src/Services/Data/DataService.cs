namespace TimeMemoria.Services;

public interface IDataService : IHostedService
{
  QuestData QuestData { get; }
  string LevequestsTitle { get; }
  string OtherQuestsTitle { get; }
  event System.Action? OnReset;
  void Reset();
  bool IsQuestComplete(Types.Quest quest);
  void UpdateQuestData();
  IReadOnlyList<ExpansionProgress> ExpansionProgress { get; }
  IReadOnlyList<CategoryProgress> CategoryProgress { get; }
  IReadOnlyList<ExpansionProgress> MsqProgress { get; }
  Types.Quest? FindOldestIncomplete(out string expansion, out string category);
}

public class DataService(ILogger _logger, Configuration _configuration, IDataManager _dataManager, IClientState _clientState) : IDataService
{
  public event System.Action? OnReset;
  public QuestData RawQuestData { get; private set; } = new();
  public QuestData QuestData { get; private set; } = new();

  /// <summary>
  /// Completion per expansion, refreshed by the same tree walk that updates the
  /// categories so it costs no additional traversal.
  /// </summary>
  public IReadOnlyList<ExpansionProgress> ExpansionProgress => _expansionProgress;

  /// <summary>The same quests grouped by journal section rather than expansion.</summary>
  public IReadOnlyList<CategoryProgress> CategoryProgress => _categoryProgress;

  /// <summary>
  /// Main Scenario completion per expansion. Read off the tree rather than
  /// tallied separately -- the tree is already nested under expansion, so each
  /// one carries its own Main Scenario section with counts on it.
  /// </summary>
  public IReadOnlyList<ExpansionProgress> MsqProgress
  {
    get
    {
      List<ExpansionProgress> result = [];

      foreach (QuestData expansion in QuestData.Categories)
      {
        QuestData? msq = expansion.Categories.FirstOrDefault((c) => c.EnglishTitle == "Main Scenario");
        if (msq is null) continue;

        result.Add(new ExpansionProgress
        {
          Id = expansion.SortKey,
          Name = expansion.Title,
          NumComplete = (int)msq.NumComplete,
          Total = (int)msq.Total
        });
      }

      return result;
    }
  }

  private readonly List<ExpansionProgress> _expansionProgress = [];
  private readonly Dictionary<uint, int[]> _expansionTally = [];

  private readonly List<CategoryProgress> _categoryProgress = [];
  private readonly Dictionary<string, int[]> _categoryTally = [];
  private readonly Dictionary<string, string> _categoryNames = [];

  /// <summary>
  /// Sections the player can choose to leave out of the totals. Kept as English
  /// names so the setting survives a language change.
  /// </summary>
  private const string LevequestsSection = "Levequests";
  private const string OtherQuestsSection = "Other Quests";

  private bool IsExcluded(string englishSection)
    => (englishSection == LevequestsSection && _configuration.ExcludeLevequests)
    || (englishSection == OtherQuestsSection && _configuration.ExcludeOtherQuests);
  private string _startArea = "";
  private string _grandCompany = "";
  private List<uint> _startClass = [];

  private readonly List<uint> _gridaniaStartQuests = [
    65621, 65659, 65660, 65564, 65737, 65981, 65664, 69390, 65711, 65661, 69391,
    65665, 65712, 65912, 65913, 65915, 65916, 65917, 65920, 65923, 65697, 65982,
    65983, 65984, 65985, 66043, 65575, 65537, 65568, 65570, 65573, 65756, 65708,
    65663, 65666, 65596, 65914
  ];

  private readonly List<uint> _gridaniaStartLeves = [
    546
  ];

  private readonly List<uint> _limsaStartQuests = [
    65644, 65645, 65998, 65999, 66079, 66001, 66002, 66003, 66004, 66005, 65933,
    65938, 65939, 65942, 65948, 65951, 65949, 65950, 66225, 66080, 66226, 66081,
    66082, 65643, 65647, 65648, 65658, 66199, 66229, 66008, 66006, 66009, 66010,
    65936, 65937, 66011, 66012, 66013, 66022, 66014, 66015, 65595, 65941
  ];

  private readonly List<uint> _limsaStartLeves = [
    556
  ];

  private readonly List<uint> _uldahStartQuests = [
    66104, 66105, 66106, 66131, 66207, 66086, 65839, 65842, 69388, 65843, 65856,
    66159, 65864, 66039, 65865, 65866, 65867, 69389, 65868, 65869, 65870, 65872,
    66164, 66087, 66177, 66088, 66064, 66209, 66130, 65925, 65926, 66223, 65594,
    65877, 66040, 66042, 65878, 66041, 65857, 65840, 65858, 65924, 65844, 65862,
    66067, 66066, 66109
  ];

  private readonly List<uint> _uldahStartLeves = [
    566
  ];

  private readonly List<uint> _twinAdderQuests = [66216, 66219, 66236, 66641, 67063, 67099, 67925];
  private readonly List<uint> _maelstromQuests = [66217, 66220, 66237, 66640, 67064, 67100, 67926];
  private readonly List<uint> _immortalFlamesQuests = [66218, 66221, 66238, 66642, 67065, 67101, 67927];

  private readonly List<uint> _arrShenanigansAdded = [];
  private readonly List<List<uint>> _arrShenanigans = [
    [65621, 65659, 65660], [65644, 65645], [66104, 66105, 66106], [65664, 69390], [65842, 69388],
    [65661, 69391], [65867, 69389], [65781, 66211], [66244, 69392], [66253, 69393], [66254, 69394],
    [66262, 69395], [66269, 69396], [66270, 69397], [66276, 69398], [66320, 69399], [66321, 69400],
    [66355, 69401], [66375, 69402], [66408, 69403], [66453, 69404], [66473, 69405], [66504, 69406],
    [66539, 69407], [66572, 70057], [66579, 69408], [66672, 69409], [66060, 70058], [66712, 69410],
    [66714, 69411], [66716, 69412], [66724, 69413], [66729, 69414], [66881, 69415], [66884, 69416],
    [66886, 69417], [66889, 69418], [66980, 69419], [66981, 69420], [66988, 69421], [65615, 69422],
    [65617, 69423], [65903, 69424], [65955, 70127], [66735, 67245], [67823, 66097], [66552, 67089],
    [65821, 65789], [65797, 65824], [66069, 66068], [66091, 66234], [65847, 65846], [65850, 65851],
    [65668, 65559], [65571, 65679], [65667, 65557], [65669, 65558], [65683, 65627], [65881, 65880],
    [65884, 65885], [65988, 65989], [65993, 65992], [68753, 69569]
  ];

  private readonly List<List<uint>> _furtherArrShenanigans = [
    [66700, 66704, 66708], [66701, 66705, 66709],
    [66702, 66706, 66710], [66699, 66703, 66707]
  ];

  private readonly List<List<uint>> _exlusiveQuests = [
    [67001, 67002, 67003], // ARR "Call of the Wild" Tribal Alliance Quests
    [69256, 69257], // YorHa "Heads or Tails"
    [69336, 69337], // Qitari "The First Stela"
    [69338, 69339], // Qitari "The Second Stela"
    [69340, 69341], // Qitari "The Third Stela"
    [66968, 66969, 66970], // An Ill-conceived Venture
    [66957, 68553], // A Self-Improving Man | If I Had a Glamour
    [66958, 68554], // Submission Impossible | Absolutely Glamourous
    [65603, 65670], // School of Hard Nocks (Retired) | Training with Leih
  ];

  private readonly List<uint> _retiredQuests = [
    65603, 65616, 65692, 65695, 65732, 65734, 65841, 65860, 65863, 65871, 65910,
    65918, 65934, 65940, 66000, 66023, 66033, 66034, 66288, 66351, 66352, 66356,
    66383, 66390, 66407, 66413, 66417, 66432, 66461, 66462, 66490, 66507, 66510,
    66575, 66578, 66582, 66713, 66715, 66717, 66718, 66719, 66720, 66721, 66722,
    66723, 66885, 66887, 66890, 66891, 66893, 66964, 66965, 66985, 66986, 66987,
    66990, 66991, 67097, 67098, 67635, 67653, 67752, 67819, 67870, 68629, 68727,
    69296, 69377, 69508, 69578, 71000, 71001, 71003, 71004
  ];

  private readonly List<uint> _retiredLeves = [
    // Actually retired Leves
    502, 519, 542, 544,
    // Leves that have no english translation in the sheets and are probably unused
    508, 514, 525, 531, 552, 554, 562, 564, 582, 597, 822, 827, 832,
  ];

  public string LevequestsTitle { get; private set; } = "";
  public string OtherQuestsTitle { get; private set; } = "";

  public Task StartAsync(CancellationToken cancellationToken)
  {
    Stopwatch phaseTimer = Stopwatch.StartNew();

    _clientState.Login += Reset;

    ClientLanguage lang = _clientState.ClientLanguage;
    _logger.Debug($"[DataService] Entered StartAsync, client state ready at +{phaseTimer.ElapsedMilliseconds}ms");

    // First Excel access of the plugin's life. Lumina opens and parses the game's
    // data files here, so this measures that rather than the loop itself.
    Lumina.Excel.ExcelSheet<JournalCategory> firstSheet = _dataManager.GetExcelSheet<JournalCategory>(ClientLanguage.English);
    _logger.Debug($"[DataService] First Excel sheet available at +{phaseTimer.ElapsedMilliseconds}ms");

    List<uint> sidequestCategories = [];
    foreach (JournalCategory journalCategory in firstSheet)
    {
      if (journalCategory.Name.ToString().Contains("Sidequests"))
      {
        sidequestCategories.Add(journalCategory.RowId);
      }
    }

    _logger.Debug($"[DataService] Sidequest categories scanned at +{phaseTimer.ElapsedMilliseconds}ms");

    Stopwatch buildTimer = Stopwatch.StartNew();

    // Each of the loops below filters a whole sheet by a single key. Doing that
    // inside the loop is O(categories x rows); grouping once up front is O(rows).
    Lumina.Excel.ExcelSheet<JournalCategory> englishCategories = _dataManager.GetExcelSheet<JournalCategory>(ClientLanguage.English);

    Dictionary<uint, List<JournalGenre>> genresByCategory = [];
    foreach (JournalGenre genre in _dataManager.GetExcelSheet<JournalGenre>(lang))
    {
      if (!genresByCategory.TryGetValue(genre.JournalCategory.RowId, out List<JournalGenre>? genreList))
        genresByCategory[genre.JournalCategory.RowId] = genreList = [];
      genreList.Add(genre);
    }

    Dictionary<uint, List<Lumina.Excel.Sheets.Quest>> questsByGenre = [];
    foreach (Lumina.Excel.Sheets.Quest q in _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Quest>(lang))
    {
      if (q.Name.IsEmpty) continue;
      if (!questsByGenre.TryGetValue(q.JournalGenre.RowId, out List<Lumina.Excel.Sheets.Quest>? questList))
        questsByGenre[q.JournalGenre.RowId] = questList = [];
      questList.Add(q);
    }

    Dictionary<uint, List<Leve>> levesByGenre = [];
    foreach (Leve l in _dataManager.GetExcelSheet<Leve>(lang))
    {
      if (l.Name.IsEmpty) continue;
      if (!levesByGenre.TryGetValue(l.JournalGenre.RowId, out List<Leve>? leveList))
        levesByGenre[l.JournalGenre.RowId] = leveList = [];
      leveList.Add(l);
    }

    _logger.Debug($"[DataService] Sheets grouped in {buildTimer.ElapsedMilliseconds}ms");

    JournalSection otherQuests = _dataManager.GetExcelSheet<JournalSection>(ClientLanguage.English).FirstOrNull(r => r.Name == "Other Quests") ?? throw new Exception("Missing 'Other Quests'.");
    string defaultMainCategory = _dataManager.GetExcelSheet<JournalSection>(lang).First(r => r.RowId == otherQuests.RowId).Name.ToString();
    string defaultMainCategoryEnglish = otherQuests.Name.ToString();

    foreach (JournalCategory journalCategory in _dataManager.GetExcelSheet<JournalCategory>(lang))
    {
      bool isSidequestCategory = sidequestCategories.Contains(journalCategory.RowId);
      string mainCategory = TrimJournalSection(journalCategory.JournalSection.ValueNullable?.Name.ToString() ?? defaultMainCategory);
      string englishMainCategory = TrimJournalSection(englishCategories.GetRow(journalCategory.RowId).JournalSection.ValueNullable?.Name.ToString() ?? defaultMainCategoryEnglish);
      string subCategory = journalCategory.Name.ToString();
      string englishSubCategory = englishCategories.GetRow(journalCategory.RowId).Name.ToString();

      if (englishMainCategory == "Levequests") LevequestsTitle = mainCategory;
      if (englishMainCategory == "Other Quests") OtherQuestsTitle = mainCategory;

      foreach (JournalGenre journalGenre in genresByCategory.GetValueOrDefault(journalCategory.RowId, []))
      {
        if (journalGenre.RowId == 0) subCategory = "Quasi-Quests";
        string section = journalGenre.Name.ToString();

        foreach (Lumina.Excel.Sheets.Quest quest in questsByGenre.GetValueOrDefault(journalGenre.RowId, []))
        {
          if (isSidequestCategory) section = quest.PlaceName.Value.Name.ToString();

          string? start = null;
          if (_gridaniaStartQuests.Contains(quest.RowId)) start = "Gridania";
          if (_limsaStartQuests.Contains(quest.RowId)) start = "Limsa Lominsa";
          if (_uldahStartQuests.Contains(quest.RowId)) start = "Ul'dah";

          // Call of the Sea. Gridania and Limsa Lominsa starts share this quest, Ul'dah has its own.
          if (quest.RowId == 66210) start = "Gridania & Limsa Lominsa";

          string? gc = null;
          if (_twinAdderQuests.Contains(quest.RowId)) gc = "Order of the Twin Adder";
          if (_maelstromQuests.Contains(quest.RowId)) gc = "Maelstrom";
          if (_immortalFlamesQuests.Contains(quest.RowId)) gc = "Immortal Flames";

          List<uint> ids = [quest.RowId];
          if (quest.Expansion.RowId == 0)
          {
            foreach (List<uint> _ids in _arrShenanigans)
            {
              if (_ids.Contains(quest.RowId))
              {
                if (_arrShenanigansAdded.Contains(quest.RowId) || quest.JournalGenre.RowId == 0) goto SkipQuest;
                ids = _ids;
                _ids.ForEach(_arrShenanigansAdded.Add);
              }
            }
            foreach (List<uint> _ids in _furtherArrShenanigans)
            {
              if (_ids.Contains(quest.RowId)) ids = _ids;
            }
          }

          AddQuest(ExpansionOf(quest.Expansion.RowId, lang), (mainCategory, englishMainCategory), (subCategory, englishSubCategory), section, new()
          {
            Title = quest.Name.ToString(),
            Ids = ids,
            Area = quest.PlaceName.Value.Name.ToString(),
            Level = quest.ClassJobLevel[0],
            SortKey = quest.SortKey,
            Gc = gc,
            Start = start,
            ExpansionId = quest.Expansion.RowId,
          }, sortKey: isSidequestCategory ? quest.PlaceName.RowId : 0);

        SkipQuest:
          continue;
        }

        foreach (Leve leve in levesByGenre.GetValueOrDefault(journalGenre.RowId, []))
        {
          section = leve.PlaceNameStart.Value.Name.ToString();

          string? start = null;
          if (_gridaniaStartLeves.Contains(leve.RowId)) start = "Gridania";
          if (_limsaStartLeves.Contains(leve.RowId)) start = "Limsa Lominsa";
          if (_uldahStartLeves.Contains(leve.RowId)) start = "Ul'dah";

          if (_retiredLeves.Contains(leve.RowId)) continue;
          AddQuest(ExpansionOf(ExpansionFromLevel(leve.ClassJobLevel), lang), (mainCategory, englishMainCategory), (subCategory, englishSubCategory), section, new()
          {
            Title = leve.Name.ToString(),
            Ids = [leve.RowId],
            Area = leve.PlaceNameStart.Value.Name.ToString(),
            Level = leve.ClassJobLevel,
            SortKey = leve.RowId,
            Start = start,
            ExpansionId = ExpansionFromLevel(leve.ClassJobLevel),
            IsLeve = true
          }, leve.PlaceNameStart.RowId);
        }
      }
    }

    // Expansions sit at the top of the tree now, ordered by release.
    RawQuestData.Categories.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));

    List<string> mainCategoryOrder = ["Main Scenario", "Chronicles of a New Era", "Sidequests", "Allied Society Quests", "Class & Job Quests", "Other Quests", "Levequests"];
    List<string> tribeOrder = [.. _dataManager.GetExcelSheet<BeastTribe>(ClientLanguage.English).Select((r) => r.NameRelation.ToString()).ToList(), "Intersocietal"];

    foreach (QuestData expansion in RawQuestData.Categories)
    {
      expansion.Categories.Sort((a, b) =>
      {
        int ia = mainCategoryOrder.IndexOf(a.EnglishTitle);
        int ib = mainCategoryOrder.IndexOf(b.EnglishTitle);
        if (ia < 0) ia = int.MaxValue;
        if (ib < 0) ib = int.MaxValue;
        if (ia != ib) return ia.CompareTo(ib);
        return string.Compare(a.EnglishTitle, b.EnglishTitle, StringComparison.OrdinalIgnoreCase);
      });

      foreach (QuestData questData in expansion.Categories)
      {
        if (questData.EnglishTitle == "Allied Society Quests")
        {
          questData.Categories.Sort((a, b) =>
          {
            string fa = a.EnglishTitle.Split(' ')[0];
            string fb = b.EnglishTitle.Split(' ')[0];
            int ia = tribeOrder.FindIndex(t => t.Contains(fa, StringComparison.OrdinalIgnoreCase));
            int ib = tribeOrder.FindIndex(t => t.Contains(fb, StringComparison.OrdinalIgnoreCase));
            if (ia < 0) ia = int.MaxValue;
            if (ib < 0) ib = int.MaxValue;
            if (ia != ib) return ia.CompareTo(ib);
            return string.Compare(a.EnglishTitle, b.EnglishTitle, StringComparison.OrdinalIgnoreCase);
          });
        }

        foreach (QuestData c1 in questData.Categories)
        {
          c1.Categories.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
          foreach (QuestData c2 in c1.Categories)
          {
            c2.Quests.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));
          }
        }
      }
    }

    QuestData = (QuestData)RawQuestData.Clone();

    buildTimer.Stop();
    _logger.Debug($"[DataService] Quest tree built in {buildTimer.ElapsedMilliseconds}ms, StartAsync total {phaseTimer.ElapsedMilliseconds}ms");

    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _clientState.Login -= Reset;

    return _logger.ServiceLifecycle();
  }

  public void Reset()
  {
    _startArea = "";
    _grandCompany = "";
    _startClass = [];
    QuestData = (QuestData)RawQuestData.Clone();
    OnReset?.Invoke();
  }

  private string TrimJournalSection(string journalSection)
  {
    return Regex.Replace(Regex.Replace(journalSection, @"\s*\([^)]*\)", ""), @"[12]\uFF08.*?\uFF09", "").Trim();
  }

  private void AddQuest((uint id, string name) expansion, (string localizedCategory, string englishCategory) category, (string localizedSubCategory, string englishSubCategory) subCategory, string section, Types.Quest quest, uint sortKey = 0)
  {
    QuestData FindOrCreateCategory(List<QuestData> list, (string localizedTitle, string englishTitle) title)
    {
      QuestData? node = list.FirstOrDefault(c => string.Equals(c.Title, title.localizedTitle, StringComparison.Ordinal));
      if (node == null)
      {
        node = new QuestData { Title = title.localizedTitle, EnglishTitle = title.englishTitle };
        list.Add(node);
      }
      return node;
    }

    // Stamp the section onto the quest so the category tally can be taken during
    // the leaf walk, where the ancestors are no longer in scope.
    quest.Section = category.localizedCategory;
    quest.EnglishSection = category.englishCategory;

    QuestData expansionNode = FindOrCreateCategory(RawQuestData.Categories, (expansion.name, expansion.name));
    expansionNode.SortKey = expansion.id;

    QuestData categoryNode = FindOrCreateCategory(expansionNode.Categories, category);
    QuestData subCategoryNode = FindOrCreateCategory(categoryNode.Categories, subCategory);
    QuestData sectionNode = FindOrCreateCategory(subCategoryNode.Categories, (section, ""));
    sectionNode.SortKey = sortKey;
    sectionNode.Quests.Add(quest);
  }

  public unsafe bool IsQuestComplete(Types.Quest quest)
  {
    if (quest.IsLeve)
      return QuestManager.Instance()->IsLevequestComplete((ushort)quest.Ids[0]);

    foreach (uint id in quest.Ids)
      if (QuestManager.IsQuestComplete(id)) return true;

    return false;
  }

  public void UpdateQuestData()
  {
    _expansionTally.Clear();
    _categoryTally.Clear();
    _categoryNames.Clear();
    UpdateQuestData(QuestData);
    RebuildExpansionProgress();
    RebuildCategoryProgress();
  }

  /// <summary>
  /// Turns the per-expansion tally into an ordered, named list. Names come from
  /// the ExVersion sheet, so they follow the client language.
  /// </summary>
  private void RebuildExpansionProgress()
  {
    _expansionProgress.Clear();

    Lumina.Excel.ExcelSheet<ExVersion> sheet = _dataManager.GetExcelSheet<ExVersion>();

    foreach (KeyValuePair<uint, int[]> entry in _expansionTally.OrderBy((e) => e.Key))
    {
      string name = sheet.GetRowOrDefault(entry.Key)?.Name.ToString() ?? "";
      if (name.Length == 0) name = entry.Key == 0 ? "A Realm Reborn" : $"Expansion {entry.Key}";

      _expansionProgress.Add(new ExpansionProgress
      {
        Id = entry.Key,
        Name = name,
        NumComplete = entry.Value[0],
        Total = entry.Value[1]
      });
    }
  }

  private void RebuildCategoryProgress()
  {
    _categoryProgress.Clear();

    foreach (KeyValuePair<string, int[]> entry in _categoryTally)
    {
      _categoryProgress.Add(new CategoryProgress
      {
        Name = _categoryNames.GetValueOrDefault(entry.Key, entry.Key),
        EnglishName = entry.Key,
        NumComplete = entry.Value[0],
        Total = entry.Value[1],
        Excluded = IsExcluded(entry.Key)
      });
    }
  }

  /// <summary>
  /// Leves have no Expansion column, so their band is taken from the level the
  /// levemete offers them at. Each expansion covers the ten levels above its cap.
  /// </summary>
  /// <summary>Expansion id paired with its display name from the ExVersion sheet.</summary>
  private (uint id, string name) ExpansionOf(uint id, ClientLanguage lang)
  {
    string name = _dataManager.GetExcelSheet<ExVersion>(lang).GetRowOrDefault(id)?.Name.ToString() ?? "";
    if (name.Length == 0) name = id == 0 ? "A Realm Reborn" : $"Expansion {id}";
    return (id, name);
  }

  private static uint ExpansionFromLevel(uint level) => level switch
  {
    <= 50 => 0,
    <= 60 => 1,
    <= 70 => 2,
    <= 80 => 3,
    <= 90 => 4,
    _ => 5
  };

  /// <summary>
  /// The first incomplete quest in tree order, which is release order — the
  /// natural "what should I do next" answer.
  /// </summary>
  public Types.Quest? FindOldestIncomplete(out string expansion, out string category)
  {
    expansion = category = "";
    return FindOldestIncomplete(QuestData, "", ref expansion, ref category);
  }

  private Types.Quest? FindOldestIncomplete(QuestData node, string path, ref string expansion, ref string category)
  {
    foreach (Types.Quest quest in node.Quests)
    {
      if (IsQuestComplete(quest)) continue;
      category = path;
      expansion = ExpansionName(quest.ExpansionId);
      return quest;
    }

    foreach (QuestData child in node.Categories)
    {
      string childPath = path.Length == 0 ? child.Title : $"{path} — {child.Title}";
      Types.Quest? found = FindOldestIncomplete(child, childPath, ref expansion, ref category);
      if (found != null) return found;
    }

    return null;
  }

  private string ExpansionName(uint id)
  {
    string name = _dataManager.GetExcelSheet<ExVersion>().GetRowOrDefault(id)?.Name.ToString() ?? "";
    if (name.Length == 0) name = id == 0 ? "A Realm Reborn" : $"Expansion {id}";
    return name;
  }

  private void UpdateQuestData(QuestData questData)
  {
    questData.NumComplete = questData.Total = 0;
    if (_startArea == "") DetermineStartArea();
    if (_grandCompany == "") DetermineGrandCompany();
    if (_startClass.Count == 0) DetermineStartClass();

    if (questData.Categories.Count > 0)
    {
      questData.Hide = true;
      foreach (QuestData category in questData.Categories)
      {
        UpdateQuestData(category);
        questData.NumComplete += category.NumComplete;
        questData.Total += category.Total;
        if (!category.Hide) questData.Hide = false;
      }
    }
    else
    {
      questData.Hide = true;
      foreach (Types.Quest? quest in questData.Quests.ToList())
      {
        if (!_startArea.IsNullOrEmpty() && !quest.Start.IsNullOrEmpty() && !quest.Start.Contains(_startArea))
        {
          if (IsQuestComplete(quest))
          {
            _logger.Error($"Quest {quest.Title} {string.Join(" ", quest.Ids)} is restricted but completed");
          }

          questData.Quests.Remove(quest);
          continue;
        }

        if (!_grandCompany.IsNullOrEmpty() && !quest.Gc.IsNullOrEmpty() && _grandCompany != quest.Gc)
        {
          if (IsQuestComplete(quest))
          {
            _logger.Error($"Quest {quest.Title} {string.Join(" ", quest.Ids)} is restricted but completed");
          }

          questData.Quests.Remove(quest);
          continue;
        }

        foreach (uint startClass in _startClass)
        {
          if (quest.Ids.Contains(startClass))
          {
            questData.Quests.Remove(quest);
            continue;
          }
        }

        foreach (List<uint> exclusive in _exlusiveQuests)
        {
          if (!exclusive.Any(quest.Ids.Contains)) continue;

          bool shouldRemove = exclusive.Any((id) => QuestManager.IsQuestComplete(id) && !quest.Ids.Contains(id));
          if (shouldRemove)
          {
            questData.Quests.Remove(quest);
            break;
          }
        }

        if (_retiredQuests.Any(quest.Ids.Contains) && !IsQuestComplete(quest))
        {
          questData.Quests.Remove(quest);
          break;
        }

        bool complete = IsQuestComplete(quest);
        if (complete) questData.NumComplete++;

        // Section counts always include everything, so an excluded section can
        // still show its real numbers while greyed out.
        if (!_categoryTally.TryGetValue(quest.EnglishSection, out int[]? sectionTally))
        {
          _categoryTally[quest.EnglishSection] = sectionTally = [0, 0];
          _categoryNames[quest.EnglishSection] = quest.Section;
        }
        sectionTally[1]++;
        if (complete) sectionTally[0]++;

        // Expansion counts honour the exclusions, so the expansion rows always
        // sum to the overall figure.
        if (!IsExcluded(quest.EnglishSection))
        {
          if (!_expansionTally.TryGetValue(quest.ExpansionId, out int[]? tally))
            _expansionTally[quest.ExpansionId] = tally = [0, 0];
          tally[1]++;
          if (complete) tally[0]++;
        }

        quest.Hide = (_configuration.DisplayOption == 1 && !IsQuestComplete(quest)) ||
                     (_configuration.DisplayOption == 2 && IsQuestComplete(quest));
        if (!quest.Hide) questData.Hide = false;
      }

      questData.Total += questData.Quests.Count;
    }
  }

  private void DetermineStartArea()
  {
    _startArea = QuestManager.IsQuestComplete(65575) ? "Gridania" :
                               QuestManager.IsQuestComplete(65643) ? "Limsa Lominsa" :
                               QuestManager.IsQuestComplete(66130) ? "Ul'dah" : "";
    _logger.Debug($"Start Area {_startArea}");
  }

  private void DetermineGrandCompany()
  {
    _grandCompany = QuestManager.IsQuestComplete(66216) ? "Order of the Twin Adder" :
                                  QuestManager.IsQuestComplete(66217) ? "Maelstrom" :
                                  QuestManager.IsQuestComplete(66218) ? "Immortal Flames" : "";
    _logger.Debug($"Grand Company {_grandCompany}");
  }

  private void DetermineStartClass()
  {
    _startClass = (
      // Gladiator
      QuestManager.IsQuestComplete(65792) && !QuestManager.IsQuestComplete(65822) ? [65822, 65713] :
      // Pugilist
      QuestManager.IsQuestComplete(66090) && !QuestManager.IsQuestComplete(66089) ? [66089, 65714] :
      // Marauder
      QuestManager.IsQuestComplete(65849) && !QuestManager.IsQuestComplete(65848) ? [65848, 65715] :
      // Lancer
      QuestManager.IsQuestComplete(65583) && !QuestManager.IsQuestComplete(65754) ? [65754, 65716] :
      // Archer
      QuestManager.IsQuestComplete(65582) && !QuestManager.IsQuestComplete(65755) ? [65755, 65717] :
      // Rogue
      QuestManager.IsQuestComplete(65640) && !QuestManager.IsQuestComplete(65638) ? [65638, 65637] :
      // Conjurer
      QuestManager.IsQuestComplete(65584) && !QuestManager.IsQuestComplete(65747) ? [65747, 65718] :
      // Thaumaturge
      QuestManager.IsQuestComplete(65883) && !QuestManager.IsQuestComplete(65882) ? [65882, 65719] :
      // Arcanist
      QuestManager.IsQuestComplete(65991) && !QuestManager.IsQuestComplete(65990) ? [65990, 65987] : []);
    _logger.Debug($"Start Class [{string.Join(", ", _startClass)}]");
  }
}
