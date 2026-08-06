using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using Lumina.Text.ReadOnly;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// The Quests tab as a native game window, laid out like its ImGui counterpart:
/// a navigation tree on the left, the selected branch's quests on the right.
///
/// The split is what makes seven thousand quests browsable. The left panel
/// holds only sections, so it stays a couple of dozen rows; the right holds one
/// section's quests, which is tens. Putting the quests in the tree instead —
/// as an earlier attempt did — gives a single list thousands of rows long, and
/// it scrolls one row per wheel tick.
/// </summary>
public unsafe class QuestBrowserAddon : NativeAddon
{
  private const float BarHeight = 28.0f;
  private const float Gap = 8.0f;

  public required IDataService DataService { get; init; }
  public required IQuestPatchService PatchService { get; init; }
  public required ILogger Logger { get; init; }

  private TextInputNode? _search;
  private NestableTreeListNode<QuestData, CategoryTreeItemNode>? _tree;
  private TextNode? _heading;
  private ListNode<Types.Quest, QuestListItemNode>? _quests;

  private string _query = "";
  private CompletionFilter _filter = CompletionFilter.All;
  private QuestData? _selected;

  private enum CompletionFilter { All, Complete, Incomplete }

  protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
  {
    base.OnSetup(addon, atkValueSpan);

    // Rows are built through a parameterless constructor, so these cannot be
    // handed to instances individually.
    QuestListItemNode.DataService = DataService;
    QuestListItemNode.PatchService = PatchService;

    float leftWidth = ContentSize.X * 4.0f / 10.0f;
    float rightWidth = ContentSize.X * 6.0f / 10.0f;
    float bodyHeight = ContentSize.Y - BarHeight * 2.0f - Gap;

    new VerticalListNode
    {
      Position = ContentStartPosition,
      Size = ContentSize,
      FitWidth = true,
      InitialNodes =
      [
        new HorizontalListNode
        {
          Height = BarHeight,
          FitHeight = true,
          Alignment = HorizontalListAnchor.Right,
          NavIndex = 1,
          NavDown = 3,
          InitialNodes =
          [
            _search = new TextInputNode
            {
              Width = ContentSize.X,
              PlaceholderString = "Search all quests . . .",
              AutoSelectAll = true,
              OnInputReceived = OnSearchChanged
            }
          ]
        },
        new HorizontalListNode
        {
          Height = BarHeight,
          FitHeight = true,
          InitialNodes =
          [
            new TabBarNode
            {
              Width = leftWidth,
              Height = BarHeight,
              NavIndex = 3,
              NavUp = 1,
              NavDown = 10,
              InitialEntries =
              [
                new TabBarEntry { Label = "All", OnClick = () => SetFilter(CompletionFilter.All) },
                new TabBarEntry { Label = "Done", OnClick = () => SetFilter(CompletionFilter.Complete) },
                new TabBarEntry { Label = "To Do", OnClick = () => SetFilter(CompletionFilter.Incomplete) }
              ]
            },
            _heading = new TextNode
            {
              Width = rightWidth,
              Height = BarHeight,
              TextFlags = TextFlags.Ellipsis,
              FontSize = 12,
              TextColor = new Vector4(0.6f, 0.8f, 1.0f, 1.0f),
              String = "Select a section on the left.",
              IsVisible = true
            }
          ]
        },
        new ResNode { Height = Gap },
        new HorizontalListNode
        {
          Height = bodyHeight,
          FitHeight = true,
          InitialNodes =
          [
            _tree = new NestableTreeListNode<QuestData, CategoryTreeItemNode>
            {
              Width = leftWidth,
              Height = bodyHeight,
              NoResultsString = "Nothing matches.",
              Sections = BuildSections(),
              OnItemSelected = OnSectionSelected
            },
            _quests = new ListNode<Types.Quest, QuestListItemNode>
            {
              Width = rightWidth,
              Height = bodyHeight,
              NavIndex = 100,
              NavLeft = 10,
              OptionsList = [],
              ShowNoResultsPlaceholder = false
            }
          ]
        }
      ]
    }.AttachNode(this);

    addon->FocusNode = _search;
  }

  protected override void OnFinalize(AtkUnitBase* addon)
  {
    _search = null;
    _tree = null;
    _heading = null;
    _quests = null;
    _selected = null;

    _query = "";
    _filter = CompletionFilter.All;

    base.OnFinalize(addon);
  }

  private void OnSectionSelected(QuestData? node)
  {
    _selected = node;
    ShowQuests();
  }

  private void SetFilter(CompletionFilter filter)
  {
    _filter = filter;

    // The tree's counts do not change with the filter, but which branches have
    // anything left in them does.
    if (_tree is not null) _tree.Sections = BuildSections();

    ShowQuests();
  }

  private void OnSearchChanged(ReadOnlySeString input)
  {
    _query = input.ToString();

    // A search is answered from the whole tree, so the left panel stops being
    // the thing that decides what is on the right.
    ShowQuests();
  }

  /// <summary>
  /// Fills the right panel: search results if there is a query, otherwise the
  /// selected branch's quests.
  /// </summary>
  private void ShowQuests()
  {
    if (_quests is null) return;

    List<Types.Quest> quests = [];

    if (_query.Length > 0)
    {
      Collect(DataService.QuestData, quests);
      if (_heading is not null) _heading.String = $"Search — {quests.Count} found";
    }
    else if (_selected is not null)
    {
      Collect(_selected, quests);
      if (_heading is not null)
        _heading.String = _selected.Total > 0
          ? $"{_selected.Title}   {(int)_selected.NumComplete}/{(int)_selected.Total}"
          : _selected.Title;
    }
    else if (_heading is not null)
    {
      _heading.String = "Select a section on the left.";
    }

    _quests.OptionsList = quests;
    _quests.ResetScroll();

    Logger.Debug($"[QuestBrowser] {quests.Count} quests — filter={_filter} query='{_query}' " +
                 $"section='{_selected?.Title ?? "none"}'");
  }

  /// <summary>
  /// The navigation tree: expansions holding categories, categories holding
  /// genres. Only branches, never quests — those belong on the right.
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
        // that does not is selectable in its own right. Either way the thing
        // you click is a branch of the quest tree, not a quest.
        if (genres.Count > 1)
          section.Children.Add(new TreeListSection<QuestData> { Header = Label(category), Entries = genres });
        else
          section.Entries.Add(category);
      }

      if (section.Entries.Count > 0 || section.Children.Count > 0) sections.Add(section);
    }

    return sections;
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
