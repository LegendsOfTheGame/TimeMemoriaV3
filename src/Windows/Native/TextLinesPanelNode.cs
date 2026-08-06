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
  private bool _built;

  protected TextLinesPanelNode()
  {
    _list = new ListNode<string, StringListItemNode>
    {
      IsVisible = true,
      OptionsList = [],
      ShowNoResultsPlaceholder = false
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

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    _list.Size = new Vector2(Width, Height);
    _list.Position = new Vector2(0.0f, 0.0f);
  }
}
