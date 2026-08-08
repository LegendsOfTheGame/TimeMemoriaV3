using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// The game's window component ships spare title bar buttons, but they belong
/// to <c>AtkComponentWindow</c>, which reapplies its own component data and
/// switches them back off. So this builds our own node and drops it into the
/// slot beside the close button.
///
/// The position comes from that button's runtime geometry rather than from the
/// literals in <c>WindowNode</c>'s source: those describe the header
/// container's frame, and a node added to the addon is not in it.
/// </summary>
internal static class TitleBarButton
{
  private static readonly Vector2 ButtonSize = new(16.0f, 16.0f);

  /// <summary>The gear sprite on the shared window button sheet.</summary>
  private static readonly Vector2 GearCoordinates = new(44.0f, 0.0f);

  /// <summary>Where the close button sits when the window node is unavailable.</summary>
  private static readonly Vector2 FallbackClosePosition = new(33.0f, 6.0f);

  private const float FallbackCloseHeight = 28.0f;

  /// <summary>Gap between the close button's left edge and this one's.</summary>
  private const float Spacing = 14.0f;

  public static TextureButtonNode Gear(WindowNodeBase? windowNode, float windowWidth, string tooltip,
    System.Action onClick)
  {
    Vector2 closePosition = new(windowWidth - FallbackClosePosition.X, FallbackClosePosition.Y);
    float closeHeight = FallbackCloseHeight;

    if (windowNode is KamiToolKit.Nodes.WindowNode window)
    {
      closePosition = new Vector2(window.CloseButtonNode.X, window.CloseButtonNode.Y);
      closeHeight = window.CloseButtonNode.Height;
    }

    return new TextureButtonNode
    {
      Size = ButtonSize,
      // Centred on the close button, which is taller than this one.
      Position = new Vector2(closePosition.X - Spacing, closePosition.Y + (closeHeight - ButtonSize.Y) / 2.0f),
      TexturePath = "ui/uld/WindowA_Button.tex",
      TextureCoordinates = GearCoordinates,
      TextureSize = ButtonSize,
      IsVisible = true,
      OnClick = onClick,
      TextTooltip = tooltip
    };
  }
}
