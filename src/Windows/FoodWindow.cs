namespace TimeMemoria.Windows;

/// <summary>
/// <c>/tm food</c>: for each role, the strongest meal you hold and where it is.
/// The full list with every stat lives in the native window's Bonuses tab; this
/// is the short answer to "what do I eat for this job".
///
/// Always the classic window, like <c>/tmwiki</c>.
/// </summary>
public class FoodWindow : Window
{
  private static readonly (string Role, string Heading, Vector4 Colour)[] Sections =
  [
    // Role colours match BonusesPanelNode and ProgressionPanelNode.
    ("DPS", "DPS Bonus", new(0.85f, 0.40f, 0.38f, 1.0f)),
    ("Healer", "Healer Bonus", new(0.35f, 0.78f, 0.47f, 1.0f)),
    ("Tank", "Tank Bonus", new(0.34f, 0.58f, 0.92f, 1.0f)),
    ("Crafter", "Crafting Bonus", new(0.72f, 0.58f, 0.88f, 1.0f)),
    ("Gatherer", "Gathering Bonus", new(0.88f, 0.72f, 0.38f, 1.0f))
  ];

  private readonly IFoodService _food;

  /// <summary>Set by WindowService: the same Settings the installer's button opens.</summary>
  public System.Action? OpenSettings { get; set; }

  public FoodWindow(IFoodService food) : base("Food Bonuses — Time Memoria##TimeMemoriaFood")
  {
    _food = food;

    // The /tmmini and /tmwiki width, so the small windows match.
    Size = new Vector2(310, 400);
    SizeCondition = ImGuiCond.FirstUseEver;
    SizeConstraints = new()
    {
      MinimumSize = new Vector2(260, 200),
      MaximumSize = new Vector2(float.MaxValue)
    };

    TitleBarButtons.Add(new TitleBarButton
    {
      Icon = FontAwesomeIcon.Cog,
      IconOffset = new Vector2(2, 1),
      ShowTooltip = () => ImGui.SetTooltip("Settings"),
      Click = (_) => OpenSettings?.Invoke()
    });
  }

  /// <summary>
  /// Re-read once a second, not every frame: the reading walks every retainer's
  /// cached inventory, and food rarely changes faster than a person can look.
  /// </summary>
  private IReadOnlyList<RolePick> _picks = [];
  private FoodReading? _reading;
  private DateTime _readAt = DateTime.MinValue;

  /// <summary>When the active food runs out, so the line can tick every frame between reads.</summary>
  private DateTime _fedUntil = DateTime.MinValue;

  public override void OnOpen() => _readAt = DateTime.MinValue;

  public override void Draw()
  {
    if (DateTime.UtcNow - _readAt > TimeSpan.FromSeconds(1))
    {
      _picks = _food.BestByRole();
      _reading = _food.Read();
      _readAt = DateTime.UtcNow;
      _fedUntil = _reading.WellFed ? _readAt.AddSeconds(_reading.RemainingSeconds) : DateTime.MinValue;
    }

    IReadOnlyList<RolePick> picks = _picks;

    if (picks.Count == 0)
    {
      ImGui.TextDisabled("Log in to a character to see your food.");
      return;
    }

    // The footer keeps its place at the bottom; the list scrolls above it.
    float footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;

    using (ImRaii.ChildDisposable child = ImRaii.Child("##foodRoles", new Vector2(0, -footer), false))
    {
      if (child.Success) DrawActive();

      if (child.Success)
        foreach ((string role, string heading, Vector4 colour) in Sections)
        {
          RolePick? pick = picks.FirstOrDefault((p) => p.Role == role);

          ImGui.TextColored(colour, heading);

          if (pick?.Name is null)
          {
            // Every meal carries the same experience bonus whatever its stats,
            // so having nothing for a role still leaves something worth eating.
            ImGui.TextDisabled("Use any food for the EXP bonus.");
          }
          else
          {
            ImGui.TextUnformatted(pick.HighQuality ? $"{pick.Name} (HQ)" : pick.Name);
            RightAligned($"Found in {pick.Where}");
          }

          ImGui.Spacing();
        }
    }

    ImGui.Separator();
    // Bags read live; the saddlebag and retainers come from the game's Search
    // for Item cache, which follows every item move the client sees.
    ImGui.TextDisabled($"Inventory as of {DateTime.Now:d MMM yyyy}");
  }

  /// <summary>
  /// What you are digesting now, in its role's colour. The time left is the same
  /// "Time remaining" line /tmmini and the Bonuses tab already show, in the same
  /// format -- the same reading in another window, not a new timer.
  /// </summary>
  private void DrawActive()
  {
    ImGui.TextUnformatted("ACTIVE BONUS");

    if (_reading is not { WellFed: true } reading)
    {
      ImGui.TextDisabled("Not fed.");
      ImGui.Spacing();
      return;
    }

    Vector4 colour = Sections.FirstOrDefault((s) => s.Role == reading.Active?.Role).Colour;
    if (colour == default) colour = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];

    string name = reading.Active is { } food
      ? (food.HighQuality ? $"{food.Name} (HQ)" : food.Name)
      : "Unknown food";

    TimeSpan left = _fedUntil - DateTime.UtcNow;
    if (left < TimeSpan.Zero) left = TimeSpan.Zero;

    ImGui.TextColored(colour, name);
    RightAligned($"{(int)left.TotalMinutes}m {left.Seconds:00}s", colour);
    ImGui.Spacing();
  }

  /// <summary>Same line, pushed to the right edge, muted unless a colour is given.</summary>
  private static void RightAligned(string text, Vector4? colour = null)
  {
    float width = ImGui.CalcTextSize(text).X;
    ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetContentRegionMax().X - width));
    if (colour is { } c) ImGui.TextColored(c, text);
    else ImGui.TextDisabled(text);
  }
}
