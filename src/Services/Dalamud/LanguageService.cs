namespace TimeMemoria.Services;

/// <summary>The languages the plugin can format for. Codes match Dalamud's.</summary>
public enum DisplayLanguage { English, German, French, Japanese }

public interface ILanguageService
{
  /// <summary>Resolved on every read, so a change in any source shows at once.</summary>
  DisplayLanguage Current { get; }
}

/// <summary>
/// Which language to format in, from the first source that names a supported
/// one: the plugin's own setting, then Dalamud's UI language, then the game
/// client's. The OS locale is not consulted: under Wine it is the least
/// trustworthy of the three, and players often run Windows and the client in
/// different languages.
///
/// Character names are no signal — the global client allows Latin letters only.
/// </summary>
public class LanguageService(Configuration _configuration, IDalamudPluginInterface _pluginInterface, IClientState _clientState) : ILanguageService
{
  /// <summary>Stored value for "follow Dalamud, then the client".</summary>
  public const string Auto = "auto";

  /// <summary>
  /// The choices both settings panels offer. Each language is named in itself,
  /// so someone who cannot read the current language can still find their own.
  /// </summary>
  public static readonly (string Code, string Label)[] Options =
  [
    (Auto, "Automatic (Dalamud, then game)"),
    ("en", "English"),
    ("de", "Deutsch"),
    ("fr", "Français"),
    ("ja", "日本語")
  ];

  public DisplayLanguage Current =>
    FromCode(_configuration.Language)
    ?? FromCode(_pluginInterface.UiLanguage)
    ?? FromClient(_clientState.ClientLanguage);

  public static DisplayLanguage? FromCode(string? code) => code switch
  {
    "en" => DisplayLanguage.English,
    "de" => DisplayLanguage.German,
    "fr" => DisplayLanguage.French,
    "ja" => DisplayLanguage.Japanese,
    _ => null
  };

  private static DisplayLanguage FromClient(ClientLanguage language) => language switch
  {
    ClientLanguage.German => DisplayLanguage.German,
    ClientLanguage.French => DisplayLanguage.French,
    ClientLanguage.Japanese => DisplayLanguage.Japanese,
    _ => DisplayLanguage.English
  };
}
