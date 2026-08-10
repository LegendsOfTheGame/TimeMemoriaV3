using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace TimeMemoria.Windows.Native;

/// <summary>
/// Character, playtime, pacing and world state — the same things the ImGui News
/// tab gathers.
///
/// Rows are pooled and rewritten each update rather than rebuilt, since most of
/// this changes as you play: pacing moves with every quest, and the feed lines
/// change when it next polls.
/// </summary>
public class NewsPanelNode : TabPanelNode
{
  private const float RowHeight = 20.0f;
  private const float LabelWidth = 190.0f;
  private const float ValueWidth = 260.0f;
  private const int MaxRows = 26;

  private static readonly Vector4 Heading = new(0.6f, 0.8f, 1.0f, 1.0f);
  private static readonly Vector4 Normal = new(1.0f, 1.0f, 1.0f, 1.0f);
  private static readonly Vector4 Muted = new(0.6f, 0.6f, 0.6f, 1.0f);

  public required IPlaytimeService Playtime { get; init; }
  public required IPacingService Pacing { get; init; }
  public required IFestivalService Festivals { get; init; }
  public required INewsService News { get; init; }
  public required IPlayerState PlayerState { get; init; }

  private readonly VerticalListNode _list;
  private readonly List<(TextNode Label, TextNode Value)> _rows = [];

  public NewsPanelNode()
  {
    _list = new VerticalListNode { ItemSpacing = 1.0f, IsVisible = true };
    _list.AttachNode(this);

    for (int i = 0; i < MaxRows; i++)
    {
      HorizontalListNode container = new()
      {
        Size = new Vector2(LabelWidth + ValueWidth, RowHeight),
        IsVisible = true
      };

      TextNode label = MakeText(LabelWidth, AlignmentType.Left);
      TextNode value = MakeText(ValueWidth, AlignmentType.Left);

      container.AddNode(label);
      container.AddNode(value);

      _list.AddNode(container);
      _rows.Add((label, value));
    }
  }

  public override void Refresh()
  {
    News.Poll();

    int row = 0;

    SetHeading(ref row, "Character");

    if (PlayerState.IsLoaded)
    {
      Set(ref row, "Commendations", PlayerState.PlayerCommendations.ToString());
      Set(ref row, "Custom Deliveries", $"rank {PlayerState.DeliveryLevel}");

      List<string> standing = [];
      if (PlayerState.IsBattleMentor) standing.Add("Battle Mentor");
      if (PlayerState.IsTradeMentor) standing.Add("Trade Mentor");
      if (PlayerState.IsMentor && standing.Count == 0) standing.Add("Mentor");
      if (PlayerState.IsNovice) standing.Add("Novice");
      if (PlayerState.IsReturner) standing.Add("Returner");

      Set(ref row, "Status", standing.Count > 0 ? string.Join(", ", standing) : "—");
    }
    else
    {
      Set(ref row, "No character loaded", "", muted: true);
    }

    SetHeading(ref row, "Playtime");

    PlaytimeRecord? record = Playtime.Current;
    if (record is not null && record.LifetimePlaytime > TimeSpan.Zero)
    {
      TimeSpan total = record.LifetimePlaytime;
      Set(ref row, "Lifetime", $"{(int)total.TotalDays}d {total.Hours}h {total.Minutes}m");

      // The figure is exactly as old as the last /playtime, and this plugin
      // will not run that for you.
      Set(ref row, "recorded",
        record.LifetimePlaytimeUpdatedUtc.HasValue
          ? Ago(DateTime.UtcNow - record.LifetimePlaytimeUpdatedUtc.Value)
          : "age unknown",
        muted: true);
    }
    else
    {
      Set(ref row, "Lifetime", "run /playtime once to record it", muted: true);
    }

    SetHeading(ref row, "Pacing");

    // "Counting since" rather than "session started": this is when the baseline
    // was taken, which a plugin reload resets. Calling it the start of the
    // session would be a small lie on any day the plugin was reloaded.
    if (Pacing.CountingSince is { } since) Set(ref row, "Counting since", since.ToString("HH:mm"));

    Set(ref row, $"This session ({Pacing.SessionQuests})", Pace(Pacing.SessionMinutesPerQuest));
    Set(ref row, "Overall", Pace(Pacing.OverallMinutesPerQuest));
    Set(ref row, "Main Scenario", Pace(Pacing.MsqMinutesPerQuest));

    SetHeading(ref row, "Active Events");

    List<ActiveFestival> festivals = Festivals.GetActive();
    if (festivals.Count == 0)
    {
      Set(ref row, "Nothing running", "", muted: true);
    }
    else
    {
      // Phase 0 is the ordinary state — most events never have another one, so
      // printing it reads as an error rather than as information. Only the
      // events that actually progress through stages say anything here.
      foreach (ActiveFestival festival in festivals)
        Set(ref row, festival.DisplayName,
          festival.Phase > 0 ? $"running — stage {festival.Phase}" : "running");
    }

    for (; row < _rows.Count; row++)
    {
      (TextNode label, TextNode value) = _rows[row];
      label.IsVisible = value.IsVisible = false;
    }
  }

  private void SetHeading(ref int row, string text)
  {
    if (row >= _rows.Count) return;

    (TextNode label, TextNode value) = _rows[row++];

    label.IsVisible = true;
    label.String = text;
    label.TextColor = Heading;

    value.IsVisible = false;
  }

  private void Set(ref int row, string name, string text, bool muted = false)
  {
    if (row >= _rows.Count) return;

    (TextNode label, TextNode value) = _rows[row++];

    label.IsVisible = value.IsVisible = true;

    label.String = name;
    value.String = text;

    label.TextColor = value.TextColor = muted ? Muted : Normal;
  }

  private static string Pace(double? minutes)
  {
    if (minutes is null or <= 0) return "—";
    if (minutes < 60) return $"{minutes.Value:F0}m per quest";

    return $"{(int)(minutes.Value / 60)}h {(int)(minutes.Value % 60)}m per quest";
  }

  private static string Ago(TimeSpan span)
  {
    if (span < TimeSpan.Zero || span.TotalMinutes < 1) return "just now";
    if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
    if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";

    return $"{(int)span.TotalDays}d ago";
  }

  protected override void OnSizeChanged()
  {
    base.OnSizeChanged();

    _list.Size = new Vector2(Width, Height);
    _list.Position = new Vector2(0.0f, 0.0f);
  }

  private static TextNode MakeText(float width, AlignmentType alignment) => new()
  {
    Size = new Vector2(width, RowHeight),
    String = "",
    AlignmentType = alignment,
    FontSize = 12,
    TextColor = Normal,
    IsVisible = true
  };
}
