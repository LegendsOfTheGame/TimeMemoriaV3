using KamiToolKit.Components.ListItemNodes;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// A panel that is a scrolling column of text.
///
/// Built on <see cref="ListNode{T,TU}"/> rather than a stack of text nodes so
/// that it scrolls and only realises as many rows as fit on screen. Help and
/// Credits are both longer than any window they will be shown in.
///
/// Lines are supplied by the subclass and only rebuilt when they change, which
/// for static content means once.
/// </summary>
public abstract class TextLinesPanelNode : TabPanelNode
{
  private readonly ListNode<string, StringListItemNode> _list;

  /// <summary>
  /// Never shown — exists only so <see cref="WrapParagraph"/> can ask the game
  /// how wide a candidate line would draw, in the same font a row actually
  /// uses (<see cref="StringListItemNode"/> takes <see cref="TextNode"/>'s
  /// defaults, so this needs none of its own).
  /// </summary>
  private readonly TextNode _measurer = new() { IsVisible = false };

  private bool _built;

  protected TextLinesPanelNode()
  {
    _measurer.AttachNode(this);

    _list = new ListNode<string, StringListItemNode>
    {
      IsVisible = true,
      ShowNoResultsPlaceholder = false,
      OptionsList = []
    };

    _list.AttachNode(this);
  }

  /// <summary>
  /// The text to show, top to bottom.
  ///
  /// Avoid blank lines. Every entry is a list row, and an empty one draws as a
  /// full-width highlighted bar rather than as whitespace — indentation does
  /// the work of separating headings from their content instead.
  /// </summary>
  protected abstract List<string> BuildLines();

  public override void OnShown()
  {
    // Services arrive through init properties, which have not run when the
    // constructor does, so the first build waits until the panel is shown.
    if (_built) return;

    _list.OptionsList = BuildLines();
    _built = true;
  }

  /// <summary>Forces the lines to be rebuilt next time this panel is shown.</summary>
  protected void Invalidate() => _built = false;

  /// <summary>
  /// Splits <paramref name="text"/> into lines that fit the row width this
  /// panel actually has, each prefixed with <paramref name="indent"/>.
  ///
  /// A row's usable width is this panel's <see cref="Width"/> minus 24: 8 for
  /// <see cref="ListNode{T,TU}"/>'s scroll bar and the 8-pixel gap it leaves
  /// beside it, then another 8 for <see cref="StringListItemNode"/>'s own
  /// left padding of its label. Wrapping to a fixed character count instead
  /// left the text unable to use a wider window or safe in a narrower one;
  /// this measures the actual font instead of guessing at it.
  /// </summary>
  protected List<string> WrapParagraph(string text, string indent = "   ")
  {
    float available = Width - 24.0f - _measurer.GetTextDrawSize(indent).X;

    List<string> lines = [];
    string current = "";

    foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
      string candidate = current.Length == 0 ? word : $"{current} {word}";

      if (current.Length > 0 && _measurer.GetTextDrawSize(candidate).X > available)
      {
        lines.Add(indent + current);
        current = word;
      }
      else
      {
        current = candidate;
      }
    }

    if (current.Length > 0) lines.Add(indent + current);

    return lines;
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    _list.Size = new Vector2(Width, Height);
    _list.Position = new Vector2(0.0f, 0.0f);
  }
}
