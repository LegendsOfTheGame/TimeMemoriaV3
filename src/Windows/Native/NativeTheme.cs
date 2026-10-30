using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// Text colours for the native windows, taken from the game's UIColor sheet for
/// whichever theme the player has picked.
///
/// The windows themselves are drawn from the game's textures, so they already
/// follow the theme -- Dark, Light, Classic FF and the five Clear themes. Our
/// text used to be fixed RGB chosen against one of them, and on Light, Clear
/// White or Clear Pink it nearly vanished. The bar is AODA, which means WCAG 2.0
/// AA: 4.5:1 contrast for text, and nothing carried by colour alone. Measured
/// against the themes, only the theme's own normal text colour (row 1) clears
/// 4.5:1 everywhere, so all text uses it; headings stand out by size and
/// spacing, and a role is written out in words beside the game's own role
/// icon. Settled with players in Discord, 26 Oct 2026.
///
/// Read on every call rather than cached: a theme change takes effect the next
/// time a panel refreshes.
/// </summary>
internal static class NativeTheme
{
  /// <summary>UIColor rows. Values per theme are in /xldata UIColor.</summary>
  private const uint NormalTextRow = 1;
  private const uint DimmedTextRow = 3;

  /// <summary>Space above every heading but the first, so categories read as groups.</summary>
  public const float HeadingGap = 8.0f;

  /// <summary>
  /// Headings stand out by font and size. The game's Bold text flag draws no
  /// differently in any of its six fonts (font test window, 26 Oct 2026), and SE
  /// switches font for emphasis instead -- so headings are Jupiter, the game's
  /// small-capitals serif, two sizes above the Axis body text.
  /// </summary>
  public const uint HeadingFontSize = 14;
  public const uint BodyFontSize = 12;

  /// <summary>The theme's normal text colour: white on dark themes, dark on light ones.</summary>
  public static Vector4 Text => ColorHelper.GetColor(NormalTextRow);

  /// <summary>
  /// The theme's dimmed text, for states the reader can also tell another way
  /// (a finished quest, an excluded category). It does not reach 4.5:1 on every
  /// theme, so it never carries information on its own.
  /// </summary>
  public static Vector4 Dimmed => ColorHelper.GetColor(DimmedTextRow);

  /// <summary>
  /// The heading font and bold flag. Jupiter without bold is what ships; the
  /// Debug-only "/tm fonttest next" command steps through the alternatives so
  /// they can be compared in game.
  /// </summary>
  public static FontType HeadingFont { get; private set; } = FontType.Jupiter;
  public static bool HeadingBold { get; private set; }

  /// <summary>Where the cycle starts: Jupiter, plain. Each font has a plain and a bold step.</summary>
  private static int _headingStyle = (int)FontType.Jupiter * 2;

  /// <summary>Moves every heading to the next font and bold combination, and names it.</summary>
  public static string NextHeadingStyle()
  {
    FontType[] fonts = Enum.GetValues<FontType>();
    _headingStyle = (_headingStyle + 1) % (fonts.Length * 2);

    HeadingFont = fonts[_headingStyle / 2];
    HeadingBold = _headingStyle % 2 == 1;

    return $"Headings: {HeadingFont}{(HeadingBold ? " + Bold" : "")} ({_headingStyle + 1} of {fonts.Length * 2})";
  }

  /// <summary>Heading or body style, set only when it changes -- panels refresh every frame.</summary>
  public static void SetHeadingSize(TextNode node, bool heading)
  {
    uint size = heading ? HeadingFontSize : BodyFontSize;
    if (node.FontSize != size) node.FontSize = size;

    FontType font = heading ? HeadingFont : FontType.Axis;
    if (node.FontType != font) node.FontType = font;

    bool bold = heading && HeadingBold;
    if (((node.TextFlags & TextFlags.Bold) != 0) != bold)
      node.TextFlags = bold ? node.TextFlags | TextFlags.Bold : node.TextFlags & ~TextFlags.Bold;
  }

  /// <summary>
  /// Lays out a pooled row as a heading (larger, with <see cref="HeadingGap"/>
  /// above unless it opens the panel) or as an ordinary row. Returns true when the
  /// row's height changed, so the caller knows the list needs laying out again.
  /// </summary>
  public static bool ShapeRow(HorizontalListNode container, TextNode label, float rowHeight, bool heading, bool first)
  {
    float gap = heading && !first ? HeadingGap : 0.0f;

    label.TextColor = Text;
    SetHeadingSize(label, heading);
    label.Y = gap;

    if (Math.Abs(container.Height - (rowHeight + gap)) < 0.01f) return false;

    container.Height = rowHeight + gap;
    return true;
  }
}
