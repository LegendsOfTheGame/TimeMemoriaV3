using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// News and What's New, sharing one tab.
///
/// The two are structurally different — News is a scrolling stat list, What's
/// New a heading plus a quest list — so this composes the two existing panels
/// unmodified rather than rebuilding either into a shared layout. News gets
/// the larger, upper share; What's New the remainder below it.
/// </summary>
public class NewsAndWhatsNewPanelNode : TabPanelNode
{
  private const float Gap = 6.0f;

  /// <summary>News is the denser, more-often-useful half — playtime, pacing,
  /// story remaining, active events — so it keeps the majority of the tab.</summary>
  private const float NewsFraction = 0.6f;

  public required NewsPanelNode News { get; init; }
  public required WhatsNewPanelNode WhatsNew { get; init; }

  /// <summary>
  /// Deferred: News/WhatsNew are required properties, not set until after this
  /// object's own constructor has already run, so they cannot be attached
  /// there. OnSizeChanged is the first point they are guaranteed populated.
  /// </summary>
  private bool _attached;

  public override void OnShown()
  {
    News.OnShown();
    WhatsNew.OnShown();
  }

  public override void Refresh()
  {
    News.Refresh();
    WhatsNew.Refresh();
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    if (!_attached)
    {
      _attached = true;
      News.AttachNode(this);
      WhatsNew.AttachNode(this);
    }

    float newsHeight = (Height - Gap) * NewsFraction;
    float whatsNewHeight = Height - Gap - newsHeight;

    News.Position = new Vector2(0.0f, 0.0f);
    News.Size = new Vector2(Width, newsHeight);

    WhatsNew.Position = new Vector2(0.0f, newsHeight + Gap);
    WhatsNew.Size = new Vector2(Width, whatsNewHeight);
  }
}
