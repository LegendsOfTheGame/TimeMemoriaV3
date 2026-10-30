#if DEBUG
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// Debug builds only: one sample heading in every game font, plain and with the
/// Bold flag, side by side in one window -- to choose a heading look by eye.
/// The Bold flag drew nothing in Axis, and MidoriKami expects it to depend on
/// the font and other flags (26 Oct 2026). Opened with "/tm fonttest". Never
/// shipped.
/// </summary>
public unsafe class FontTestAddon : NativeAddon
{
  private const string Sample = "Combat food (15)";
  private const float RowHeight = 30.0f;
  private const float NameWidth = 130.0f;
  private const float SampleWidth = 200.0f;

  protected override void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
  {
    base.OnSetup(addon, atkValueSpan);

    float y = ContentStartPosition.Y;

    AddText("Font", ContentStartPosition.X, y, NameWidth, FontType.Axis, bold: false, size: 12);
    AddText("Plain", ContentStartPosition.X + NameWidth, y, SampleWidth, FontType.Axis, bold: false, size: 12);
    AddText("Bold flag", ContentStartPosition.X + NameWidth + SampleWidth, y, SampleWidth, FontType.Axis, bold: false, size: 12);
    y += 22.0f;

    foreach (FontType font in Enum.GetValues<FontType>())
    {
      AddText(font.ToString(), ContentStartPosition.X, y, NameWidth, FontType.Axis, bold: false, size: 12);
      AddText(Sample, ContentStartPosition.X + NameWidth, y, SampleWidth, font, bold: false, size: NativeTheme.HeadingFontSize);
      AddText(Sample, ContentStartPosition.X + NameWidth + SampleWidth, y, SampleWidth, font, bold: true, size: NativeTheme.HeadingFontSize);
      y += RowHeight;
    }

    // Jupiter one and two sizes above body text, each over a body line, the
    // way a heading sits over its rows.
    y += 6.0f;
    foreach (uint size in new uint[] { NativeTheme.BodyFontSize + 1, NativeTheme.BodyFontSize + 2 })
    {
      AddText($"Jupiter {size}", ContentStartPosition.X, y, NameWidth, FontType.Axis, bold: false, size: 12);
      AddText(Sample, ContentStartPosition.X + NameWidth, y, SampleWidth * 2, FontType.Jupiter, bold: false, size: size);
      y += 22.0f;
      AddText("2x Kaiser Roll (HQ)   Tank · Tenacity +10% (Cap 36)", ContentStartPosition.X + NameWidth, y,
        SampleWidth * 2, FontType.Axis, bold: false, size: NativeTheme.BodyFontSize);
      y += RowHeight;
    }
  }

  private void AddText(string text, float x, float y, float width, FontType font, bool bold, uint size)
  {
    TextNode node = new()
    {
      Position = new Vector2(x, y),
      Size = new Vector2(width, RowHeight),
      String = text,
      FontType = font,
      FontSize = size,
      TextColor = NativeTheme.Text,
      IsVisible = true
    };

    if (bold) node.TextFlags |= TextFlags.Bold;

    AddNode(node);
  }
}
#endif
