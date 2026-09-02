using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using Lumina.Text.ReadOnly;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// Search, a completion filter, a navigation tree, and the selected branch's
/// quests.
///
/// The split is what makes seven thousand quests browsable. The tree holds only
/// branches, so it stays a couple of dozen rows; the list holds one branch's
/// quests, which is tens. Putting quests in the tree gives a single list
/// thousands of rows long, and it scrolls one row per wheel tick.
/// </summary>
public class QuestsPanelNode : TabPanelNode
{
  private const float BarHeight = 28.0f;
  private const float Gap = 8.0f;

  public required IDataService DataService { get; init; }
  public required IQuestPatchService PatchService { get; init; }
  public required IQuestMapService MapService { get; init; }
  public required IClassJobProgressService ProgressService { get; init; }
  public required ILogger Logger { get; init; }
  public required Configuration Config { get; init; }

  private readonly TextInputNode _search;
  private readonly TextButtonNode _wiki;
  private readonly TextButtonNode _clear;
  private readonly TabBarNode _filterTabs;
  private readonly NestableTreeListNode<QuestData, CategoryTreeItemNode> _tree;
  private readonly TextNode _heading;
  private readonly ListNode<Types.Quest, QuestListItemNode> _list;

  private string _query = "";
  private CompletionFilter _filter = CompletionFilter.All;
  private QuestData? _selected;

  /// <summary>
  /// The job-quest setting the tree was last built against, so a change to it
  /// can be noticed.
  ///
  /// The sections are cut once and kept, which is right for a tree the player is
  /// scrolling and selecting in — rebuilding throws their position away. But it
  /// also meant a setting that changes what belongs in "Oldest unfinished" did
  /// nothing visible until the window was closed and reopened, since switching
  /// tabs does not rebuild. Nullable so the first show always builds.
  /// </summary>
  private bool? _builtWithJobQuests;

  private enum CompletionFilter { All, Complete, Incomplete }

  public QuestsPanelNode()
  {
    _search = new TextInputNode
    {
      PlaceholderString = "Search all quests . . .",
      AutoSelectAll = true,
      IsVisible = true,
      OnInputReceived = OnSearchChanged
    };
    _search.AttachNode(this);

    // The plugin says what is left; the wiki says how to do it. Passing the
    // same words across saves typing them twice.
    //
    // With the box empty this opens the Main Scenario index rather than sitting
    // inert, so the tooltip has to say which of the two it will do.
    _wiki = new TextButtonNode
    {
      String = "Wiki",
      IsVisible = true,
      TextTooltip = WikiTooltip(string.Empty),
      OnClick = () => Windows.MainWindow.OpenWikiSearch(_query)
    };
    _wiki.AttachNode(this);

    _clear = new TextButtonNode
    {
      String = "Clear",
      IsVisible = true,
      TextTooltip = "Clear the search.",
      OnClick = ClearSearch
    };
    _clear.AttachNode(this);

    _filterTabs = new TabBarNode
    {
      Height = BarHeight,
      IsVisible = true,
      InitialEntries =
      [
        new TabBarEntry { Label = "All", OnClick = () => SetFilter(CompletionFilter.All) },
        new TabBarEntry { Label = "Done", OnClick = () => SetFilter(CompletionFilter.Complete) },
        new TabBarEntry { Label = "To Do", OnClick = () => SetFilter(CompletionFilter.Incomplete) }
      ]
    };
    _filterTabs.AttachNode(this);

    _heading = new TextNode
    {
      TextFlags = TextFlags.Ellipsis,
      FontSize = 12,
      TextColor = new Vector4(0.6f, 0.8f, 1.0f, 1.0f),
      String = "Select a section on the left.",
      IsVisible = true
    };
    _heading.AttachNode(this);

    _tree = new NestableTreeListNode<QuestData, CategoryTreeItemNode>
    {
      NoResultsString = "Nothing matches.",
      IsVisible = true,
      Sections = [],
      OnItemSelected = OnSectionSelected
    };
    _tree.AttachNode(this);

    _list = new ListNode<Types.Quest, QuestListItemNode>
    {
      IsVisible = true,
      ShowNoResultsPlaceholder = false,
      OptionsList = []
    };
    _list.AttachNode(this);
  }

  /// <summary>
  /// Built here rather than in the constructor because the services are set
  /// through init properties, which have not run when the constructor does.
  /// </summary>
  public override void OnShown()
  {
    QuestListItemNode.DataService = DataService;
    QuestListItemNode.PatchService = PatchService;
    QuestListItemNode.MapService = MapService;

    // Rebuilt when it has never been built, or when the setting it depends on
    // has moved since. Not on every show: that would cost the player their
    // scroll position and selection every time they glanced at another tab.
    if (_tree.Sections.Count == 0 || _builtWithJobQuests != Config.ShowJobQuestsInOldest)
    {
      _builtWithJobQuests = Config.ShowJobQuestsInOldest;
      _tree.Sections = BuildSections();
    }
  }

  private void OnSectionSelected(QuestData? node)
  {
    _selected = node;
    ShowQuests();
  }

  private void SetFilter(CompletionFilter filter)
  {
    _filter = filter;

    // The counts do not change with the filter, but which branches still hold
    // anything does.
    _tree.Sections = BuildSections();
    ShowQuests();
  }

  private static string WikiTooltip(string query) => query.Trim().Length == 0
    ? "Open the FFXIV wiki's Main Scenario index. Type something first to search instead."
    : "Search the FFXIV wiki. Shorthand works too — A8S, TEA, DRS.";

  private void OnSearchChanged(ReadOnlySeString input)
  {
    _query = input.ToString();
    _wiki.TextTooltip = WikiTooltip(_query);
    ShowQuests();
  }

  /// <summary>
  /// Empties the box as well as the query. Clearing only the query would leave
  /// the text sitting there looking as though it were still filtering.
  /// </summary>
  private void ClearSearch()
  {
    _search.String = string.Empty;
    _query = string.Empty;
    _wiki.TextTooltip = WikiTooltip(_query);

    ShowQuests();
  }

  /// <summary>
  /// Search results if there is a query, otherwise the selected branch. A
  /// search answers from the whole tree — naming a quest should not require
  /// finding its section first.
  /// </summary>
  private void ShowQuests()
  {
    List<Types.Quest> quests = [];

    if (_query.Length > 0)
    {
      Collect(DataService.QuestData, quests);
      _heading.String = $"Search — {quests.Count} found";
    }
    else if (_selected is not null)
    {
      Collect(_selected, quests);
      _heading.String = _selected.Total > 0
        ? $"{_selected.Title}   {(int)_selected.NumComplete}/{(int)_selected.Total}"
        : _selected.Title;
    }
    else
    {
      _heading.String = "Select a section on the left.";
    }

    _list.OptionsList = quests;
    _list.ResetScroll();
  }

  /// <summary>
  /// Expansions holding categories, categories holding genres. Only branches —
  /// quests belong in the list on the right.
  /// </summary>
  private List<TreeListSection<QuestData>> BuildSections()
  {
    List<TreeListSection<QuestData>> sections = [];

    if (BuildRecommended() is { } recommended) sections.Add(recommended);

    foreach (QuestData expansion in DataService.QuestData.Categories)
    {
      if (expansion.Hide) continue;

      TreeListSection<QuestData> section = new() { Header = Label(expansion) };

      foreach (QuestData category in expansion.Categories)
      {
        if (category.Hide) continue;

        List<QuestData> genres = [.. category.Categories.Where((g) => !g.Hide)];

        // A category that splits into genres becomes a branch holding them; one
        // that does not is selectable itself, so there is never a click leading
        // to a single identical child.
        if (genres.Count > 1)
          section.Children.Add(new TreeListSection<QuestData> { Header = Label(category), Entries = genres });
        else
          section.Entries.Add(category);
      }

      if (section.Entries.Count > 0 || section.Children.Count > 0) sections.Add(section);
    }

    return sections;
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    float leftWidth = Width * 4.0f / 10.0f;
    float rightWidth = Width - leftWidth - Gap;
    float bodyHeight = Height - BarHeight * 2.0f - Gap;

    const float buttonWidth = 70.0f;

    _search.Size = new Vector2(Width - (buttonWidth + Gap) * 2.0f, BarHeight);
    _search.Position = new Vector2(0.0f, 0.0f);

    _wiki.Size = new Vector2(buttonWidth, BarHeight);
    _wiki.Position = new Vector2(Width - (buttonWidth + Gap) - buttonWidth, 0.0f);

    _clear.Size = new Vector2(buttonWidth, BarHeight);
    _clear.Position = new Vector2(Width - buttonWidth, 0.0f);

    _filterTabs.Size = new Vector2(leftWidth, BarHeight);
    _filterTabs.Position = new Vector2(0.0f, BarHeight);

    _heading.Size = new Vector2(rightWidth, BarHeight);
    _heading.Position = new Vector2(leftWidth + Gap, BarHeight);

    _tree.Size = new Vector2(leftWidth, bodyHeight);
    _tree.Position = new Vector2(0.0f, BarHeight * 2.0f + Gap);

    _list.Size = new Vector2(rightWidth, bodyHeight);
    _list.Position = new Vector2(leftWidth + Gap, BarHeight * 2.0f + Gap);
  }

  /// <summary>
  /// What to do next, at the top of the tree where it is seen before anything
  /// else. Three answers, because "what next" has three reasonable readings:
  /// the oldest thing left undone, the story, and whichever jobs are actually
  /// being played.
  ///
  /// Each branch here holds a synthetic node wrapping quests rather than a real
  /// branch of the tree, so selecting it shows just those.
  ///
  /// Cut when the window opens and when the filter changes, not every frame.
  /// Rebuilding means reassigning the tree's sections, which rebuilds the whole
  /// node and takes selection and scroll position with it — a list that
  /// re-orders under the cursor while it is being read is worse than one that is
  /// honestly a snapshot. Little is lost: the bundles hold the real quest
  /// objects and each row re-reads its own completion when it draws, so a quest
  /// finished with the window open greys within the recount. Membership is the
  /// snapshot; the state of each row stays live.
  /// </summary>
  private TreeListSection<QuestData>? BuildRecommended()
  {
    // Everything in this section is by definition unfinished — the oldest thing
    // left, the next story quest, the job lines still outstanding. Under the
    // Done filter every branch opens onto an empty pane, and with the list's
    // no-results placeholder switched off that renders as blankness rather than
    // as "nothing matches", which reads as a broken window. Better not to offer
    // it. Exempting these bundles from the filter instead was rejected: that
    // makes the filter lie about what it is showing.
    if (_filter == CompletionFilter.Complete) return null;

    TreeListSection<QuestData> section = new() { Header = "Recommended" };

    // The real, shared quest instances — not clones. Nothing here may touch
    // Title: see the job quests below, which clone precisely because they
    // retitle, and would otherwise rename the quest everywhere it appears.
    List<Types.Quest> oldest = [.. DataService.OldestIncomplete.Select((o) => o.Quest)];

    if (oldest.Count > 0)
      section.Entries.Add(Bundle($"Oldest unfinished  ({oldest.Count})", oldest));

    if (FirstIncompleteIn("Main Scenario") is { } msq)
      section.Entries.Add(Bundle("Next in the Main Scenario", [msq]));

    // Only jobs the character has actually unlocked. An unlocked job is one with
    // a level, which is what IsUnlocked means — so jobs never touched do not
    // appear and suggest work that cannot be started.
    // Labelled with the job, since a list of fourteen quest names says nothing
    // about which job each belongs to. The label goes on a clone: the quest in
    // the tree is shared, and renaming it there would rename it everywhere.
    // A job's next quest is often above its current level, and listing it as a
    // recommendation says "do this" about something that cannot be accepted.
    // Those become an instruction instead, and sort below the ones that can be
    // started — so whatever sits at the top of the section is always actionable.
    List<(Types.Quest Quest, bool Gated)> next = [];

    foreach (ClassJobProgress job in ProgressService.GetProgress().Where((p) => p.IsUnlocked))
      if (FirstIncompleteForJob(job) is { } quest)
        next.Add(quest.Level > job.Level
          ? (GatedFor(quest, job), true)
          : (LabelledFor(quest, job.Name, OutstandingForJob(job)), false));

    // Level ascending within each group rather than across the whole list: a
    // sort purely by level would put a gated Lv 70 above a Lv 73 you can start
    // today, which is the thing this is meant to stop.
    List<Types.Quest> jobQuests =
      [.. next.OrderBy((n) => n.Gated).ThenBy((n) => n.Quest.Level).Select((n) => n.Quest)];

    if (jobQuests.Count > 0) section.Entries.Add(Bundle($"Job Quests  ({jobQuests.Count})", jobQuests));
    else LogJobLineShape();

    return section.Entries.Count > 0 ? section : null;
  }

  /// <summary>
  /// Dumps the shape of the class and job branch when no job quest was matched,
  /// so the mismatch can be seen rather than guessed at.
  /// </summary>
  private void LogJobLineShape()
  {
    Logger.Debug("[Quests] No job quests matched. Jobs: " +
                 string.Join(", ", ProgressService.GetProgress().Where((p) => p.IsUnlocked)
                   .Select((p) => p.ClassName is null ? p.Name : $"{p.Name}<{p.ClassName}>")));

    foreach (QuestData expansion in DataService.QuestData.Categories)
      foreach (QuestData category in expansion.Categories)
      {
        Logger.Debug($"[Quests]   {expansion.Title} / '{category.Title}' english='{category.EnglishTitle}' " +
                     $"children={category.Categories.Count} quests={category.Quests.Count}");

        if (category.EnglishTitle != "Class & Job Quests") continue;

        foreach (QuestData genre in category.Categories)
          Logger.Debug($"[Quests]       genre '{genre.Title}' hide={genre.Hide} " +
                       $"children={genre.Categories.Count} quests={genre.Quests.Count}");
      }
  }

  /// <summary>
  /// A copy of a quest with the job appended to its title. Everything else —
  /// ids, level, area — is carried over, so completion and patch still resolve
  /// against the real quest.
  /// </summary>
  private static Types.Quest LabelledFor(Types.Quest quest, string jobName, int outstanding)
  {
    Types.Quest copy = (Types.Quest)quest.Clone();

    // The count is only worth saying when it is more than the quest already
    // named: "1 behind" is what the row is showing anyway.
    copy.Title = outstanding > 1
      ? $"{quest.Title}  ({jobName} — {outstanding} behind)"
      : $"{quest.Title}  ({jobName})";

    return copy;
  }

  /// <summary>
  /// The same quest, titled as the thing the player can actually act on. The
  /// quest itself is unreachable until the job catches up, so naming it as a
  /// recommendation would be advice that cannot be taken — the levelling is the
  /// recommendation. The quest is still carried underneath, so its level, area
  /// and patch line up as usual and it remains selectable.
  /// </summary>
  private static Types.Quest GatedFor(Types.Quest quest, ClassJobProgress job)
  {
    Types.Quest copy = (Types.Quest)quest.Clone();
    copy.Title = $"Level {job.Name} to {quest.Level} for \"{quest.Title}\"";

    return copy;
  }

  /// <summary>
  /// A synthetic branch holding the recommended quests, so the tree stays a list
  /// of things to select and the quests themselves appear on the right like
  /// every other selection.
  ///
  /// Total is left at zero deliberately: these are suggestions, not a set being
  /// worked through, so a completion percentage beside them would be noise.
  /// </summary>
  private static QuestData Bundle(string title, List<Types.Quest> quests) => new()
  {
    Title = title,
    EnglishTitle = title,
    Quests = quests
  };

  /// <summary>First unfinished quest in a named section, across expansions in order.</summary>
  private Types.Quest? FirstIncompleteIn(string englishTitle)
  {
    foreach (QuestData expansion in DataService.QuestData.Categories)
      foreach (QuestData category in expansion.Categories)
        if (category.EnglishTitle == englishTitle && FirstIncomplete(category) is { } quest)
          return quest;

    return null;
  }

  /// <summary>
  /// First unfinished quest of a job's line, walked in expansion order so the
  /// earliest outstanding one wins.
  ///
  /// Matched against the class name as well as the job name. The journal files
  /// levels 1–30 under the class — "Marauder Quests" — and only the job's own
  /// line above that, so a level 20 Warrior's outstanding quests are all under
  /// a name the job search would never find. The class is checked first because
  /// it is the earlier of the two.
  /// </summary>
  private Types.Quest? FirstIncompleteForJob(ClassJobProgress job)
  {
    foreach (string name in job.ClassName is null ? [job.Name] : new[] { job.ClassName, job.Name })
      if (FirstIncompleteInJobLine(name) is { } quest)
        return quest;

    return null;
  }

  /// <summary>
  /// Class and job quests are not filed directly under their category. They sit
  /// beneath a role grouping — "Disciple of War Quests" holds Gladiator,
  /// Marauder and the rest; "Disciple of War Job Quests" holds Paladin, Warrior
  /// and so on. So the branch named after a job has to be searched for at any
  /// depth rather than expected at a fixed one.
  /// </summary>
  private Types.Quest? FirstIncompleteInJobLine(string name)
  {
    foreach (QuestData expansion in DataService.QuestData.Categories)
      foreach (QuestData category in expansion.Categories)
      {
        if (category.EnglishTitle != "Class & Job Quests") continue;

        if (FindBranchNamed(category, name) is { } branch && FirstIncomplete(branch) is { } quest)
          return quest;
      }

    return null;
  }

  private static QuestData? FindBranchNamed(QuestData node, string name)
  {
    foreach (QuestData child in node.Categories)
    {
      if (child.Hide) continue;

      if (child.Title.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return child;
      if (FindBranchNamed(child, name) is { } found) return found;
    }

    return null;
  }

  private Types.Quest? FirstIncomplete(QuestData node)
  {
    if (node.Hide) return null;

    foreach (Types.Quest quest in node.Quests)
      if (!quest.Hide && !DataService.IsQuestComplete(quest))
        return quest;

    foreach (QuestData child in node.Categories)
      if (FirstIncomplete(child) is { } found)
        return found;

    return null;
  }

  /// <summary>
  /// How many of a job's quests are outstanding at or below its current level.
  ///
  /// "What is next" and "how far behind am I" are different questions, and a
  /// row showing only the next quest looks the same for a job that is one quest
  /// behind and one that skipped its entire early line — a Goldsmith at 50 can
  /// still be missing the level 20 quest. Quests above the current level are not
  /// counted: those are not skipped, just not reached yet.
  /// </summary>
  private int OutstandingForJob(ClassJobProgress job)
  {
    int total = 0;

    foreach (string name in job.ClassName is null ? [job.Name] : new[] { job.ClassName, job.Name })
      foreach (QuestData expansion in DataService.QuestData.Categories)
        foreach (QuestData category in expansion.Categories)
        {
          if (category.EnglishTitle != "Class & Job Quests") continue;

          if (FindBranchNamed(category, name) is { } branch) total += CountIncompleteUpTo(branch, job.Level);
        }

    return total;
  }

  private int CountIncompleteUpTo(QuestData node, int level)
  {
    if (node.Hide) return 0;

    int total = 0;

    foreach (Types.Quest quest in node.Quests)
      if (!quest.Hide && quest.Level <= level && !DataService.IsQuestComplete(quest))
        total++;

    foreach (QuestData child in node.Categories) total += CountIncompleteUpTo(child, level);

    return total;
  }

  private static string Label(QuestData node)
    => node.Total > 0
      ? $"{node.Title}   {(int)node.NumComplete}/{(int)node.Total}   {node.NumComplete / node.Total:P0}"
      : node.Title;

  private bool Include(Types.Quest quest)
  {
    if (quest.Hide) return false;
    if (_query.Length > 0 && !quest.Title.Contains(_query, StringComparison.OrdinalIgnoreCase)) return false;

    bool complete = DataService.IsQuestComplete(quest);
    if (_filter == CompletionFilter.Complete && !complete) return false;
    if (_filter == CompletionFilter.Incomplete && complete) return false;

    return true;
  }

  private void Collect(QuestData node, List<Types.Quest> into)
  {
    if (node.Hide) return;

    foreach (Types.Quest quest in node.Quests)
      if (Include(quest))
        into.Add(quest);

    foreach (QuestData child in node.Categories) Collect(child, into);
  }
}
