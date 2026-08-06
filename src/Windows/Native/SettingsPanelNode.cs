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

  private readonly VerticalListNode _list;
  private readonly StringDropDownNode _display;
  private readonly CheckboxNode _showCount;
  private readonly CheckboxNode _showPercentage;
  private readonly CheckboxNode _excludeOther;
  private readonly CheckboxNode _excludeLeves;
  private readonly CheckboxNode _spoiler;
  private readonly CheckboxNode _freeTrial;

  public SettingsPanelNode()
  {
    _list = new VerticalListNode { ItemSpacing = 6.0f, IsVisible = true };
    _list.AttachNode(this);

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
        DataService.UpdateQuestData();
      }
    };

    _list.AddNode(_display);

    _showCount = AddCheckbox("Show count", (value) => { Config.ShowCount = value; Config.Save(); });
    _showPercentage = AddCheckbox("Show percentage", (value) => { Config.ShowPercentage = value; Config.Save(); });

    _excludeOther = AddCheckbox("Exclude 'Other Quests' from Overall", (value) =>
    {
      Config.ExcludeOtherQuests = value;
      Config.Save();
      DataService.UpdateQuestData();
    });

    _excludeLeves = AddCheckbox("Exclude 'Levequests' from Overall", (value) =>
    {
      Config.ExcludeLevequests = value;
      Config.Save();
      DataService.UpdateQuestData();
    });

    _spoiler = AddCheckbox("Spoiler Mode (show expansions you have not reached)",
      (value) => { Config.SpoilerMode = value; Config.Save(); });

    _freeTrial = AddCheckbox("Free Trial Mode (restrict to Stormblood and earlier)",
      (value) => { Config.FreeTrialMode = value; Config.Save(); });
  }

  public override void Refresh()
  {
    Sync(_showCount, Config.ShowCount);
    Sync(_showPercentage, Config.ShowPercentage);
    Sync(_excludeOther, Config.ExcludeOtherQuests);
    Sync(_excludeLeves, Config.ExcludeLevequests);
    Sync(_spoiler, Config.SpoilerMode);
    Sync(_freeTrial, Config.FreeTrialMode);

    string expected = DisplayOptions[Math.Clamp(Config.DisplayOption, 0, DisplayOptions.Length - 1)];
    if (_display.SelectedOption != expected) _display.SelectedOption = expected;
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    _list.Size = new Vector2(Width, Height);
    _list.Position = new Vector2(0.0f, 0.0f);

    foreach (CheckboxNode box in
      new[] { _showCount, _showPercentage, _excludeOther, _excludeLeves, _spoiler, _freeTrial })
      box.Size = new Vector2(Width - 12.0f, RowHeight);
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

    _list.AddNode(node);
    return node;
  }
}
