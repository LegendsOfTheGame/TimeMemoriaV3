using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Controllers;
using KamiToolKit.Enums;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// An eye button on the quest turn-in window (JournalResult), just right of its
/// "Reward" heading. A click opens /tm food, which lists the food your accepted
/// quests give and the role each meal suits -- so the reward choice can be made
/// with the role in view.
///
/// This is the Prime Directive amendment of 30 Oct 2026, and nothing more: one
/// button, one window, a manual click that opens the plugin's own window. It
/// changes no game data and opens nothing by itself. The pattern is VanillaPlus's
/// eye on the Cosmic Exploration HUD (CosmicExplorationProgressWindow, by
/// salanth357): an AddonController, a node attached on setup, disposed on close.
/// MemoriaProbe's "journaleye" probe proved it in game the same day.
///
/// Owned by NativeUiService, not the Feature list: features start off, and the
/// eye has to be gone before KamiToolKit's own cleanup, which NativeUiService's
/// disposal already comes before.
/// </summary>
public unsafe class TurnInFoodEye(Configuration _configuration, IFoodService _food, FoodWindow _foodWindow)
  : IDisposable
{
  /// <summary>
  /// Just right of "Reward", Haven's pick in game. JournalResult is 496x410 at
  /// scale 1, and the eye moves and scales with it as one of its nodes.
  /// </summary>
  private static readonly Vector2 EyePosition = new(124.0f, 131.0f);

  // JournalResult's AtkValues, from MemoriaProbe dumps on 30 Oct 2026
  // ("A Full Stomach and Happy Heart"): base reward item ids at [11..17] (0 is
  // Experience, 1 is Gil), choice reward count at [81], their ids at [82..86].
  private const int BaseIds = 11;
  private const int BaseSlots = 7;
  private const int OptionCount = 81;
  private const int OptionIds = 82;
  private const int OptionSlots = 5;

  private AddonController? _controller;
  private CircleButtonNode? _eye;

  /// <summary>Main thread only.</summary>
  public void Enable()
  {
    _controller ??= new AddonController
    {
      AddonName = "JournalResult",
      OnSetup = Attach,
      OnFinalize = Detach
    };

    _controller.Enable();
  }

  /// <summary>
  /// The setting is read each time the window opens, so turning it off or on
  /// takes effect at the next turn-in. A quest whose rewards hold no food gets
  /// no eye.
  /// </summary>
  private void Attach(AtkUnitBase* addon)
  {
    Detach(addon);

    if (!_configuration.TurnInFoodEye || !RewardsFood(addon)) return;

    _eye = new CircleButtonNode
    {
      Icon = CircleButtonIcon.Eye,
      Size = new Vector2(28.0f),
      Position = EyePosition,
      OnClick = OpenFood,
      TextTooltip = "Your food, and the role each meal suits (/tm food)"
    };

    _eye.AttachNode(addon);
  }

  private void Detach(AtkUnitBase* _)
  {
    _eye?.Dispose();
    _eye = null;
  }

  private bool RewardsFood(AtkUnitBase* addon)
  {
    Span<AtkValue> values = new(addon->AtkValues, addon->AtkValuesCount);
    if (values.Length < OptionIds + OptionSlots) return false;

    for (int slot = 0; slot < BaseSlots; slot++)
      if (IsMeal(values[BaseIds + slot])) return true;

    uint choices = values[OptionCount].Type == AtkValueType.UInt ? values[OptionCount].UInt : 0;
    for (int slot = 0; slot < Math.Min(choices, (uint)OptionSlots); slot++)
      if (IsMeal(values[OptionIds + slot])) return true;

    return false;
  }

  private bool IsMeal(AtkValue value) => value.Type == AtkValueType.UInt && _food.IsMeal(value.UInt);

  /// <summary>Opens, never closes: a second click on the eye should not hide the list.</summary>
  private void OpenFood()
  {
    _foodWindow.IsOpen = true;
    _foodWindow.BringToFront();
  }

  /// <summary>Main thread only, and before KamiToolKitLibrary.Dispose.</summary>
  public void Dispose()
  {
    GC.SuppressFinalize(this);

    _controller?.Dispose();
    _controller = null;

    _eye?.Dispose();
    _eye = null;
  }
}
