using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// The settings, which the ImGui window also owns — so this is a view of the
/// configuration rather than the owner of it.
///
/// That is why <see cref="Refresh"/> writes a control only when its value has
/// drifted from the configuration. Assigning every frame would fight a click
/// still being processed, and a change made in the other window has to show up
/// here regardless.
/// </summary>
public class SettingsPanelNode : TabPanelNode
{
  private const float RowHeight = 28.0f;

  private static readonly string[] DisplayOptions = ["Show All", "Show Complete", "Show Incomplete"];

  public required Configuration Config { get; init; }
  public required IDataService DataService { get; init; }

  /// <summary>
  /// The feature rows are built when this is assigned, not in the constructor:
  /// the object initializer sets it only after the constructor returns, and the
  /// rows need the list of features. The Ko-fi footer is added after them, so
  /// it stays last.
  /// </summary>
  public required IFeatureService Features
  {
    get => _features;
    init
    {
      _features = value;
      AddFeatureRows();
      AddFooter();
    }
  }

  private readonly IFeatureService _features = null!;
  private readonly List<(Feature Feature, CheckboxNode Box, TextButtonNode Open)> _featureRows = [];

  private readonly ScrollingNode<VerticalListNode> _scroll;
  private readonly StringDropDownNode _display;
  private readonly StringDropDownNode _language;
  private readonly CheckboxNode _showCount;
  private readonly CheckboxNode _showPercentage;
  private readonly CheckboxNode _excludeOther;
  private readonly CheckboxNode _excludeLeves;
  private readonly CheckboxNode _showJobQuestsInOldest;
  private readonly CheckboxNode _showLevequestsInOldest;
  private readonly CheckboxNode _companionAlwaysVisible;
  private readonly CheckboxNode _spoiler;
  private readonly CheckboxNode _freeTrial;
  private readonly CheckboxNode _useNative;
  private TextNode _resizeHint = null!;
  private TextButtonNode _kofi = null!;

  private const string KofiUrl = "https://ko-fi.com/legendsofthegame";

  public SettingsPanelNode()
  {
    // The callbacks below close over Config and DataService, which are `required`
    // and therefore not yet assigned at this point in construction — the object
    // initializer sets them only after this constructor returns. The compiler
    // correctly can't prove that, hence CS8602 on every reference. It is safe in
    // practice: KamiToolKit only invokes OnClick/OnOptionSelected in response to
    // user input, never synchronously during construction, so every callback
    // fires long after the required properties are set.
#pragma warning disable CS8602
    // The dropdown sits above the scroll, not inside it: the scroll clips its
    // content, and an open option list would be cut off at the scroll's edge.
    _display = new StringDropDownNode
    {
      Size = new Vector2(220.0f, RowHeight),
      IsVisible = true,
      MaxListOptions = DisplayOptions.Length,
      Options = [.. DisplayOptions],
      OnOptionSelected = (option) =>
      {
        Config.DisplayOption = Math.Max(0, Array.IndexOf(DisplayOptions, option));
        Config.Save();
        DataService.UpdateQuestData(true);
      }
    };

    _display.AttachNode(this);

    // Beside the display dropdown, above the scroll, for the same clipping reason.
    _language = new StringDropDownNode
    {
      Size = new Vector2(260.0f, RowHeight),
      IsVisible = true,
      MaxListOptions = LanguageService.Options.Length,
      Options = [.. LanguageService.Options.Select(o => o.Label)],
      OnOptionSelected = (option) =>
      {
        int index = Array.FindIndex(LanguageService.Options, o => o.Label == option);
        Config.Language = LanguageService.Options[Math.Max(0, index)].Code;
        Config.Save();
      }
    };

    _language.AttachNode(this);

    // The list outgrew the window once the Ko-fi button joined it. ContentNode
    // properties are set before Size, as ScrollingNode requires.
    _scroll = new ScrollingNode<VerticalListNode> { IsVisible = true, AutoHideScrollBar = true };
    _scroll.ContentNode.ItemSpacing = 6.0f;
    _scroll.ContentNode.FitContents = true;
    _scroll.AttachNode(this);

    _showCount = AddCheckbox("Show count", (value) => { Config.ShowCount = value; Config.Save(); });
    _showPercentage = AddCheckbox("Show percentage", (value) => { Config.ShowPercentage = value; Config.Save(); });

    _excludeOther = AddCheckbox("Exclude 'Other Quests' from Overall", (value) =>
    {
      Config.ExcludeOtherQuests = value;
      Config.Save();
      DataService.UpdateQuestData(true);
    });

    _excludeLeves = AddCheckbox("Exclude 'Levequests' from Overall", (value) =>
    {
      Config.ExcludeLevequests = value;
      Config.Save();
      DataService.UpdateQuestData(true);
    });

    // Forces a recount, like the two exclusions above: the shortlist is built
    // during the tree walk, so it does not change until the next one.
    _showJobQuestsInOldest = AddCheckbox("Show job quests in 'Oldest unfinished'", (value) =>
    {
      Config.ShowJobQuestsInOldest = value;
      Config.Save();
      DataService.UpdateQuestData(true);
    });

    _showLevequestsInOldest = AddCheckbox("Show levequests in 'Oldest unfinished'", (value) =>
    {
      Config.ShowLevequestsInOldest = value;
      Config.Save();
      DataService.UpdateQuestData(true);
    });

    _companionAlwaysVisible = AddCheckbox("Keep /tmmini visible when the game hides the UI",
      (value) => { Config.CompanionAlwaysVisible = value; Config.Save(); });

    _spoiler = AddCheckbox("Spoiler Mode (show expansions you have not reached)",
      (value) => { Config.SpoilerMode = value; Config.Save(); });

    _freeTrial = AddCheckbox("Free Trial Mode (restrict to Shadowbringers and earlier)",
      (value) => { Config.FreeTrialMode = value; Config.Save(); });

    // Present in both windows deliberately: whichever one you are looking at,
    // you can switch to the other. Only offering it in the ImGui window would
    // strand anyone who chose this one.
    _useNative = AddCheckbox("Use the native look for /tm",
      (value) => { Config.UseNativeUi = value; Config.Save(); });
#pragma warning restore CS8602
  }

  /// <summary>
  /// One checkbox per optional feature, with its description as the tooltip,
  /// and an Open button under it while the feature is on.
  /// </summary>
  private void AddFeatureRows()
  {
    _scroll.ContentNode.AddNode(new TextNode
    {
      Size = new Vector2(360.0f, 22.0f),
      FontSize = 14,
      String = "Features — optional, each off until you turn it on",
      IsVisible = true
    });

    foreach (Feature feature in _features.Features)
    {
      CheckboxNode box = AddCheckbox(feature.DisplayName, (value) => _features.SetEnabled(feature, value));
      box.TextTooltip = feature.Description;

      TextButtonNode open = new()
      {
        Size = new Vector2(220.0f, RowHeight),
        String = $"Open {feature.DisplayName}",
        IsVisible = feature.IsEnabled && feature.CommandArgument is not null,
        OnClick = feature.Open
      };

      _scroll.ContentNode.AddNode(open);
      _featureRows.Add((feature, box, open));
    }
  }

  private void AddFooter()
  {
    // This window has no resize handle -- the toolkit does not provide one --
    // so the only way to change its size is to size the classic window and come
    // back. Said here because there is nothing on screen to suggest it.
    _resizeHint = new TextNode
    {
      FontSize = 11,
      TextColor = NativeTheme.Text,
      String = "If this window is the wrong size, resize the classic window and reopen this one.",
      IsVisible = true
    };

    _scroll.ContentNode.AddNode(_resizeHint);

    // Last in the list, below every real setting: it is offered, never asked
    // for, and opens the default browser only when clicked.
    _kofi = new TextButtonNode
    {
      Size = new Vector2(180.0f, RowHeight),
      String = "Support on Ko-fi",
      IsVisible = true,
      OnClick = () => Dalamud.Utility.Util.OpenLink(KofiUrl)
    };

    _scroll.ContentNode.AddNode(_kofi);
  }

  public override void Refresh()
  {
    Sync(_showCount, Config.ShowCount);
    Sync(_showPercentage, Config.ShowPercentage);
    Sync(_excludeOther, Config.ExcludeOtherQuests);
    Sync(_excludeLeves, Config.ExcludeLevequests);
    Sync(_showJobQuestsInOldest, Config.ShowJobQuestsInOldest);
    Sync(_showLevequestsInOldest, Config.ShowLevequestsInOldest);
    Sync(_companionAlwaysVisible, Config.CompanionAlwaysVisible);
    Sync(_spoiler, Config.SpoilerMode);
    Sync(_freeTrial, Config.FreeTrialMode);
    Sync(_useNative, Config.UseNativeUi);

    // A feature switched on or off in either window shows or hides its Open
    // button here; the list then closes the gap and the scroll range follows.
    bool relayout = false;
    foreach ((Feature feature, CheckboxNode box, TextButtonNode open) in _featureRows)
    {
      Sync(box, feature.IsEnabled);

      bool showOpen = feature.IsEnabled && feature.CommandArgument is not null;
      if (open.IsVisible == showOpen) continue;

      open.IsVisible = showOpen;
      relayout = true;
    }

    if (relayout)
    {
      _scroll.ContentNode.RecalculateLayout();
      _scroll.RecalculateSizes();
    }

    string expected = DisplayOptions[Math.Clamp(Config.DisplayOption, 0, DisplayOptions.Length - 1)];
    if (_display.SelectedOption != expected) _display.SelectedOption = expected;

    int language = Math.Max(0, Array.FindIndex(LanguageService.Options, o => o.Code == Config.Language));
    string expectedLanguage = LanguageService.Options[language].Label;
    if (_language.SelectedOption != expectedLanguage) _language.SelectedOption = expectedLanguage;
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    _display.Position = new Vector2(0.0f, 0.0f);
    _language.Position = new Vector2(_display.Width + 12.0f, 0.0f);

    float scrollY = RowHeight + 6.0f;
    _scroll.Size = new Vector2(Width, Height - scrollY);
    _scroll.Position = new Vector2(0.0f, scrollY);

    foreach (CheckboxNode box in
      new[] { _showCount, _showPercentage, _excludeOther, _excludeLeves, _showJobQuestsInOldest,
        _showLevequestsInOldest, _companionAlwaysVisible, _spoiler, _freeTrial, _useNative })
      box.Size = new Vector2(Width - 24.0f, RowHeight);

    foreach ((_, CheckboxNode box, _) in _featureRows)
      box.Size = new Vector2(Width - 24.0f, RowHeight);

    _resizeHint.Size = new Vector2(Width - 24.0f, 18.0f);

    // The content never changes height at run time, so one measure per resize
    // is enough to set the scroll range.
    _scroll.RecalculateSizes();
  }

  private static void Sync(CheckboxNode node, bool value)
  {
    if (node.IsChecked != value) node.IsChecked = value;
  }

  private CheckboxNode AddCheckbox(string label, Action<bool> onClick)
  {
    CheckboxNode node = new()
    {
      Size = new Vector2(360.0f, RowHeight),
      String = label,
      IsVisible = true,
      OnClick = onClick
    };

    _scroll.ContentNode.AddNode(node);
    return node;
  }
}
