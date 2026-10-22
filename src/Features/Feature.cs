namespace TimeMemoria.Features;

/// <summary>
/// An optional part of the plugin that the player switches on or off in
/// Settings, the way VanillaPlus lists its modifications. Off by default: a
/// feature appears only for a player who asked for it.
///
/// Modelled on VanillaPlus's GameModification with two differences. The stored
/// key is a property, not the class name, so renaming a class cannot silently
/// switch a feature off for everyone. And enabling is synchronous, because
/// nothing here hooks the game; a feature adds a window and nothing more.
/// </summary>
public abstract class Feature
{
  /// <summary>
  /// Stored in <see cref="Configuration.EnabledFeatures"/>. Never rename one:
  /// a saved config holds the old key, and the feature would turn off.
  /// </summary>
  public abstract string Key { get; }

  public abstract string DisplayName { get; }

  public abstract string Description { get; }

  /// <summary>The argument that opens this feature from chat, as in
  /// <c>/tm bestiary</c>. Null when the feature has nothing to open.</summary>
  public virtual string? CommandArgument => null;

  /// <summary>
  /// Slash commands of their own, such as <c>/bmap</c>, for a feature that
  /// replaces a separate plugin. Registered whether or not the feature is on:
  /// typing one is a clear request, so it switches the feature on and opens it.
  /// </summary>
  public virtual string[] SlashCommands => [];

  public bool IsEnabled { get; internal set; }

  /// <summary>Called on the framework thread.</summary>
  protected internal abstract void OnEnable();

  /// <summary>Called on the framework thread. Must be safe to call after a
  /// failed <see cref="OnEnable"/>.</summary>
  protected internal abstract void OnDisable();

  /// <summary>Opens the feature's window. Only called while enabled.</summary>
  public virtual void Open() { }

  /// <summary>What one of <see cref="SlashCommands"/> does once the feature
  /// is on. Opens the feature's window unless a feature says otherwise.</summary>
  public virtual void OnSlashCommand() => Open();
}
