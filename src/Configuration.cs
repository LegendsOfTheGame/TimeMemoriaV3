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
