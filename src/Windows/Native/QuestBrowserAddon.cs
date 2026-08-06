using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;
using Lumina.Text.ReadOnly;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// The Quests tab as a native game window: search across the top, filter tabs
/// and a scrolling quest list on the left, the selected quest's details on the
/// right.
///
/// Unlike the earlier native tabs this one is built declaratively, in a single
/// nested expression, which is the shape the toolkit is designed around. Layout
/// is expressed as fractions of the window's own content area rather than fixed
/// pixel constants, so resizing does not have to be handled separately.
///
/// The completion filter appears here as a tab row rather than a setting. It is
/// the same value the Settings tab exposes, just promoted to where it is
/// actually used.
/// </summary>
public unsafe class QuestBrowserAddon : NativeAddon
{
  private const float BarHeight = 28.0f;
  private const float Gap = 12.0f;

  public required IDataService DataService { get; init; }
  public required IQuestPatchService PatchService { get; init; }

  private TextInputNode? _search;
  private ListNode<Types.Quest, QuestListItemNode>? _list;
  private QuestInfoNode? _info;

  private string _query = "";
  private CompletionFilter _filter = CompletionFilter.All;

  private enum CompletionFilter { All, Complete, Incomplete }

  protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
  {
    base.OnSetup(addon, atkValueSpan);

    // The list builds its rows through a parameterless constructor, so these
    // cannot be injected per instance.
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
              PlaceholderString = "Search quests . . .",
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
                  NavDown = 6,
                  InitialEntries =
                  [
                    new TabBarEntry { Label = "All", OnClick = () => SetFilter(CompletionFilter.All) },
                    new TabBarEntry { Label = "Done", OnClick = () => SetFilter(CompletionFilter.Complete) },
                    new TabBarEntry { Label = "To Do", OnClick = () => SetFilter(CompletionFilter.Incomplete) }
                  ]
                },
                new ResNode { Height = Gap },
                _list = new ListNode<Types.Quest, QuestListItemNode>
                {
                  Height = ContentSize.Y - BarHeight * 2.0f - Gap,
                  NavIndex = 6,
                  NavUp = 3,
                  NavRight = 100,
                  OptionsList = GetQuests(),
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
    _list = null;
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
    if (_list is null) return;

    _list.OptionsList = GetQuests();
    _list.ResetScroll();

    // The previous selection may not survive the new filter, and leaving its
    // details on screen beside a list that no longer contains it reads as a bug.
    _info?.SetQuest(null);
  }

  /// <summary>
  /// Every quest the tree holds, flattened, then filtered. Walking the tree
  /// rather than keeping a parallel list means the expansion and category
  /// hiding already applied there is inherited rather than reimplemented.
  /// </summary>
  private List<Types.Quest> GetQuests()
  {
    List<Types.Quest> quests = [];

    foreach (QuestData expansion in DataService.QuestData.Categories)
      foreach (QuestData category in expansion.Categories)
        foreach (QuestData genre in category.Categories)
          foreach (Types.Quest quest in genre.Quests)
          {
            if (quest.Hide) continue;
            if (_query.Length > 0 && !quest.Title.Contains(_query, StringComparison.OrdinalIgnoreCase)) continue;

            bool complete = DataService.IsQuestComplete(quest);
            if (_filter == CompletionFilter.Complete && !complete) continue;
            if (_filter == CompletionFilter.Incomplete && complete) continue;

            quests.Add(quest);
          }

    return quests;
  }
}
