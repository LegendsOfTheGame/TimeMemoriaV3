using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// A small icon button for list rows, whose icon fills the button.
///
/// KamiToolKit's <see cref="IconButtonNode"/> insets its icon 8 px on every
/// side to clear the frame's border, and its hover/press timelines pin the
/// icon at that offset. On a 22 px row button that leaves a 6 px icon nobody
/// can read. This is the same frame and the same timelines with a 2 px inset,
/// so the icon draws over the frame border instead of inside it.
/// </summary>
public class RowIconButtonNode : ButtonBase
{
  private const float Inset = 2.0f;

  private NineGridNode BackgroundNode { get; }
  private IconImageNode ImageNode { get; }

  public uint IconId
  {
    get => ImageNode.IconId;
    set => ImageNode.IconId = value;
  }

  public RowIconButtonNode()
  {
    // Same frame part IconButtonNode uses, so the row buttons still look like
    // the game's own.
    BackgroundNode = new SimpleNineGridNode
    {
      TexturePath = "ui/uld/BgParts.tex",
      TextureSize = new Vector2(32.0f, 32.0f),
      TextureCoordinates = new Vector2(33.0f, 65.0f),
      TopOffset = 8.0f,
      LeftOffset = 8.0f,
      RightOffset = 8.0f,
      BottomOffset = 8.0f,
    };
    BackgroundNode.AttachNode(this);

    ImageNode = new IconImageNode
    {
      TextureSize = new Vector2(32.0f, 32.0f),
      FitTexture = true,
    };
    ImageNode.AttachNode(this);

    LoadThreePartTimelines(this, BackgroundNode, ImageNode, new Vector2(Inset, Inset));

    InitializeComponentEvents();
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    BackgroundNode.Size = Size;
    ImageNode.Size = Size - new Vector2(Inset * 2.0f, Inset * 2.0f);
    ImageNode.Position = new Vector2(Inset, Inset);
  }
}
