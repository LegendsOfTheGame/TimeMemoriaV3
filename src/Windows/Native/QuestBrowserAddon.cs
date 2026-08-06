using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using Lumina.Text.ReadOnly;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// The Quests tab as a native game window: search across the top, a collapsible
/// quest tree on the left, and details of the selected quest on the right.
///
/// A tree rather than a flat list because there are around seven thousand
/// quests and the list scrolls one row per wheel tick — no amount of tuning
/// makes that browsable. Collapsed, a whole section costs a single row.
///
/// <see cref="TreeListSection{T}"/> has a header, its own entries and child
/// sections, which is the same shape as the quest tree this plugin already
/// builds. So the sections are a direct projection of that tree rather than a
/// separate model kept in step with it.
/// </summary>
public unsafe class QuestBrowserAddon : NativeAddon
{
  private const float BarHeight = 28.0f;
  private const float Gap = 8.0f;

  public required IDataService DataService { get; init; }
  public required IQuestPatchService PatchService { get; init; }
  public required ILogger Logger { get; init; }

  private TextInputNode? _search;
  private NestableTreeListNode<Types.Quest, QuestListItemNode>? _tree;
  private QuestInfoNode? _info;

  private string _query = "";
  private CompletionFilter _filter = CompletionFilter.All;

  private enum CompletionFilter { All, Complete, Incomplete }

  protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
  {
    base.OnSetup(addon, atkValueSpan);

    // Rows are built through a parameterless constructor, so these cannot be
    // handed to instances individually.
    QuestListItemNode.DataService = DataService;
    QuestListItemNode.PatchService = PatchService;

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
          Height = ContentSize.Y - BarHeight,
          FitHeight = true,
          InitialNodes =
          [
            new VerticalListNode
            {
              Width = ContentSize.X * 4.5f / 10.0f,
              FitWidth = true,
              InitialNodes =
              [
                new TabBarNode
                {
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
                new ResNode { Height = Gap },
                _tree = new NestableTreeListNode<Types.Quest, QuestListItemNode>
                {
                  Height = ContentSize.Y - BarHeight * 2.0f - Gap,
                  NoResultsString = "No quests match.",
                  Sections = BuildSections(),
                  OnItemSelected = (quest) => _info?.SetQuest(quest)
                }
              ]
            },
            _info = new QuestInfoNode
            {
              Width = ContentSize.X * 5.5f / 10.0f,
              PatchService = PatchService,
              DataService = DataService
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
    _info = null;

    _query = "";
    _filter = CompletionFilter.All;

    base.OnFinalize(addon);
  }

  private void SetFilter(CompletionFilter filter)
  {
    _filter = filter;
    Refresh();
  }

  private void OnSearchChanged(ReadOnlySeString input)
  {
    _query = input.ToString();
    Refresh();
  }

  private void Refresh()
  {
    if (_tree is null) return;

    _tree.Sections = BuildSections();
    _tree.ResetScroll();

    // The previous selection may not survive the new filter, and leaving its
    // details beside a tree that no longer contains it reads as a bug.
    _info?.SetQuest(null);
  }

  /// <summary>
  /// The quest tree projected into sections.
  ///
  /// Categories are gathered across expansions, so "Main Scenario" appears once
  /// rather than six times — the genres beneath already name their era, as in
  /// "Heavensward Main Scenario Quests", so an expansion level above them would
  /// only add a click.
  ///
  /// A search collapses all of that into one flat section: if you can already
  /// name a quest, you should not have to find its category first.
  /// </summary>
  private List<TreeListSection<Types.Quest>> BuildSections()
  {
    if (_query.Length > 0)
    {
      List<Types.Quest> matches = [];
      Collect(DataService.QuestData, matches);

      Logger.Debug($"[QuestBrowser] search '{_query}' matched {matches.Count}");

      return matches.Count == 0
        ? []
        : [new TreeListSection<Types.Quest> { Header = $"Results  ({matches.Count})", Entries = matches }];
    }

    Dictionary<string, TreeListSection<Types.Quest>> byCategory = [];
    List<TreeListSection<Types.Quest>> sections = [];

    foreach (QuestData expansion in DataService.QuestData.Categories)
    {
      if (expansion.Hide) continue;

      foreach (QuestData category in expansion.Categories)
      {
        if (category.Hide) continue;

        if (!byCategory.TryGetValue(category.EnglishTitle, out TreeListSection<Types.Quest>? section))
        {
          byCategory[category.EnglishTitle] = section = new TreeListSection<Types.Quest> { Header = category.Title };
          sections.Add(section);
        }

        Project(category, section);
      }
    }

    // A section whose whole subtree was filtered away is noise, not information.
    sections.RemoveAll(IsEmpty);

    return sections;
  }

  /// <summary>Copies one tree node's quests and children onto a section.</summary>
  private void Project(QuestData node, TreeListSection<Types.Quest> section)
  {
    foreach (Types.Quest quest in node.Quests)
      if (Include(quest))
        section.Entries.Add(quest);

    foreach (QuestData child in node.Categories)
    {
      if (child.Hide) continue;

      TreeListSection<Types.Quest> childSection = new() { Header = Label(child) };
      Project(child, childSection);

      if (!IsEmpty(childSection)) section.Children.Add(childSection);
    }
  }

  private static bool IsEmpty(TreeListSection<Types.Quest> section)
    => section.Entries.Count == 0 && section.Children.Count == 0;

  /// <summary>Header carrying the counts, since that is why one section gets opened over another.</summary>
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
