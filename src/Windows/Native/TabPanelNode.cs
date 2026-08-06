using KamiToolKit.Nodes.Simplified;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// One tab's worth of content inside the native window.
///
/// The four tabs were separate addons first, which proved each piece worked but
/// meant four windows to open and close. As panels they are built once and
/// shown or hidden by the tab bar, so the window keeps a single frame and a
/// single lifetime.
/// </summary>
public abstract class TabPanelNode : SimpleComponentNode
{
  /// <summary>
  /// Called on the game's update, but only for the panel currently on screen.
  /// A hidden panel has nothing worth recomputing.
  /// </summary>
  public virtual void Refresh() { }

  /// <summary>Called when this panel becomes the visible one.</summary>
  public virtual void OnShown() { }
}
