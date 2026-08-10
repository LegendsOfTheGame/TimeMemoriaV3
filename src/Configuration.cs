namespace TimeMemoria;

[Serializable]
public class Configuration : IPluginConfiguration
{
  public int Version { get; set; } = 0;
  public int DisplayOption { get; set; } = 0;
  public bool ShowCount { get; set; } = true;
  public bool ShowPercentage { get; set; } = true;
  public bool ExcludeOtherQuests { get; set; } = false;
  public bool ExcludeLevequests { get; set; } = false;

  /// <summary>Show story the character has not reached yet.</summary>
  public bool SpoilerMode { get; set; } = false;

  /// <summary>Restrict to what a free trial account can actually play.</summary>
  public bool FreeTrialMode { get; set; } = false;

  /// <summary>Keyed by "CharacterName@WorldName".</summary>
  public Dictionary<string, PlaytimeRecord> PlaytimeRecords { get; set; } = [];

  /// <summary>
  /// Collectable counts per character, keyed the same way playtime is.
  ///
  /// Persisted because the number only appears when the Achievements window is
  /// opened. Forgetting it on logout would mean the display is blank far more
  /// often than not.
  /// </summary>
  public Dictionary<string, StoredReadings> AchievementReadings { get; set; } = [];

  /// <summary>Open the game-styled window rather than the ImGui one.</summary>
  public bool UseNativeUi { get; set; } = false;

  /// <summary>
  /// Keep the at-a-glance window visible when the game hides its interface.
  ///
  /// Off by default, and deliberately: the plugin does not put itself in front
  /// of anything you did not ask it to. But the game hides addons during quest
  /// turn-ins, which is exactly when the numbers change, and someone who wants
  /// it up regardless should be able to have that.
  ///
  /// It is the general hide-the-UI mechanism rather than a turn-in specific one,
  /// so this also keeps the window up during cutscenes and screenshots.
  /// </summary>
  public bool CompanionAlwaysVisible { get; set; } = false;

  /// <summary>
  /// Size for the native window, which has no resize handle of its own. Tracked
  /// from the ImGui window, which does, and persisted so the native window does
  /// not depend on the other having been opened this session.
  ///
  /// The default is a size actually settled on in use rather than a guess: the
  /// quest tree and its list both fit without scrolling sideways, and the eight
  /// tabs fit on one row.
  /// </summary>
  public float NativeWindowWidth { get; set; } = 956f;

  /// <inheritdoc cref="NativeWindowWidth"/>
  public float NativeWindowHeight { get; set; } = 689f;

  [NonSerialized]
  private IDalamudPluginInterface PluginInterface = null!;

  public void Initialize(IDalamudPluginInterface pluginInterface)
  {
    PluginInterface = pluginInterface;
  }

  public void Save()
  {
    PluginInterface.SavePluginConfig(this);
  }
}
