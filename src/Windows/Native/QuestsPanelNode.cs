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

  private readonly TextInputNode _search;
  private readonly TabBarNode _filterTabs;
  private readonly NestableTreeListNode<QuestData, CategoryTreeItemNode> _tree;
  private readonly TextNode _heading;
  private readonly ListNode<Types.Quest, QuestListItemNode> _list;

  private string _query = "";
  private CompletionFilter _filter = CompletionFilter.All;
  private QuestData? _selected;

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
      OptionsList = [],
      ShowNoResultsPlaceholder = false
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

    if (_tree.Sections.Count == 0) _tree.Sections = BuildSections();
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

  private void OnSearchChanged(ReadOnlySeString input)
  {
    _query = input.ToString();
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

    _search.Size = new Vector2(Width, BarHeight);
    _search.Position = new Vector2(0.0f, 0.0f);

    _filterTabs.Size = new Vector2(leftWidth, BarHeight);
    _filterTabs.Position = new Vector2(0.0f, BarHeight);

    _heading.Size = new Vector2(rightWidth, BarHeight);
    _heading.Position = new Vector2(leftWidth + Gap, BarHeight);

    _tree.Size = new Vector2(leftWidth, bodyHeight);
    _tree.Position = new Vector2(0.0f, BarHeight * 2.0f + Gap);

    _list.Size = new Vector2(rightWidth, bodyHeight);
    _list.Position = new Vector2(leftWidth + Gap, BarHeight * 2.0f + Gap);
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
