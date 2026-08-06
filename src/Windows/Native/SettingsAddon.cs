using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// The Settings tab as a native game window, and the first native tab that
/// takes input rather than only showing text.
///
/// That changes the update model. The read-only windows can rewrite their nodes
/// freely each frame; here a node is also something the player is interacting
/// with, so <see cref="OnUpdate"/> writes a control only when its value has
/// actually drifted from the configuration. Assigning every frame would fight
/// the click that is still being processed.
///
/// The same settings can be changed from the ImGui window, so this stays a view
/// of the configuration rather than an owner of it.
/// </summary>
public unsafe class SettingsAddon : NativeAddon
{
  private const float RowHeight = 28.0f;
  private const float ControlWidth = 340.0f;

  private static readonly string[] DisplayOptions = ["Show All", "Show Complete", "Show Incomplete"];

  public required Configuration Config { get; init; }
  public required IDataService DataService { get; init; }

  private StringDropDownNode? _display;
  private CheckboxNode? _showCount;
  private CheckboxNode? _showPercentage;
  private CheckboxNode? _excludeOther;
  private CheckboxNode? _excludeLeves;
  private CheckboxNode? _spoiler;
  private CheckboxNode? _freeTrial;

  protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
  {
    VerticalListNode list = new()
    {
      Position = ContentStartPosition,
      Size = ContentSize,
      ItemSpacing = 4.0f,
      IsVisible = true
    };

    AddNode(list);

    _display = new StringDropDownNode
    {
      Size = new Vector2(200.0f, RowHeight),
      IsVisible = true,
      MaxListOptions = DisplayOptions.Length,
      Options = [.. DisplayOptions],
      SelectedOption = DisplayOptions[Math.Clamp(Config.DisplayOption, 0, DisplayOptions.Length - 1)],
      OnOptionSelected = (option) =>
      {
        Config.DisplayOption = Array.IndexOf(DisplayOptions, option);
        Config.Save();
        DataService.UpdateQuestData();
      }
    };

    list.AddNode(_display);

    _showCount = AddCheckbox(list, "Show count", Config.ShowCount, (value) =>
    {
      Config.ShowCount = value;
      Config.Save();
    });

    _showPercentage = AddCheckbox(list, "Show percentage", Config.ShowPercentage, (value) =>
    {
      Config.ShowPercentage = value;
      Config.Save();
    });

    _excludeOther = AddCheckbox(list, "Exclude 'Other Quests' from Overall", Config.ExcludeOtherQuests, (value) =>
    {
      Config.ExcludeOtherQuests = value;
      Config.Save();
      DataService.UpdateQuestData();
    });

    _excludeLeves = AddCheckbox(list, "Exclude 'Levequests' from Overall", Config.ExcludeLevequests, (value) =>
    {
      Config.ExcludeLevequests = value;
      Config.Save();
      DataService.UpdateQuestData();
    });

    _spoiler = AddCheckbox(list, "Spoiler Mode", Config.SpoilerMode, (value) =>
    {
      Config.SpoilerMode = value;
      Config.Save();
    });

    _freeTrial = AddCheckbox(list, "Free Trial Mode", Config.FreeTrialMode, (value) =>
    {
      Config.FreeTrialMode = value;
      Config.Save();
    });
  }

  /// <summary>
  /// Only writes a control that has drifted, so a value changed in the ImGui
  /// window shows up here without stamping over an interaction in progress.
  /// </summary>
  protected override void OnUpdate(AtkUnitBase* addon)
  {
    Sync(_showCount, Config.ShowCount);
    Sync(_showPercentage, Config.ShowPercentage);
    Sync(_excludeOther, Config.ExcludeOtherQuests);
    Sync(_excludeLeves, Config.ExcludeLevequests);
    Sync(_spoiler, Config.SpoilerMode);
    Sync(_freeTrial, Config.FreeTrialMode);

    if (_display is not null)
    {
      string expected = DisplayOptions[Math.Clamp(Config.DisplayOption, 0, DisplayOptions.Length - 1)];
      if (_display.SelectedOption != expected) _display.SelectedOption = expected;
    }
  }

  private static void Sync(CheckboxNode? node, bool value)
  {
    if (node is not null && node.IsChecked != value) node.IsChecked = value;
  }

  private CheckboxNode AddCheckbox(VerticalListNode list, string label, bool initial, Action<bool> onClick)
  {
    CheckboxNode node = new()
    {
      Size = new Vector2(ControlWidth, RowHeight),
      String = label,
      IsChecked = initial,
      IsVisible = true,
      OnClick = onClick
    };

    list.AddNode(node);
    return node;
  }
}
