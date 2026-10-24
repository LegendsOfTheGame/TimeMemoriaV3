namespace TimeMemoria.Services;

public interface IExportHistoryService
{
  /// <summary>
  /// Keeps a copy of an export on disk and hands the same text back, so a
  /// caller can wrap its return value in this. Never throws.
  /// </summary>
  string Keep(string kind, string json);
}

/// <summary>
/// The last few exports per character, kept as files in the plugin's config
/// folder.
///
/// An import replaces what the reader held, so a reading that looks wrong
/// afterwards cannot be checked against the one before it. On 24 Oct 2026 a
/// collections export read 202 duty clears where the true count was 192, and
/// that list was gone by the time anyone asked which ten were extra. Keeping
/// the exports here means the next odd reading can be diffed.
///
/// Files, not the Dalamud log: the log is capped at 10 MB and a full export is
/// about 23 KB. Local only, like the exports themselves.
/// </summary>
public class ExportHistoryService(IPluginLog _pluginLog, IDalamudPluginInterface _pluginInterface)
  : IExportHistoryService
{
  /// <summary>
  /// Per character, all kinds together. A busy session made about fifteen
  /// exports in an hour, so three would have lost them within minutes.
  /// </summary>
  private const int Slots = 5;

  private string Folder => Path.Combine(_pluginInterface.ConfigDirectory.FullName, "exports");

  public string Keep(string kind, string json)
  {
    try
    {
      string? character = CharacterOf(json);
      if (character == null) return json;

      Directory.CreateDirectory(Folder);
      string prefix = $"{SafeName(character)}.";

      // UTC, sortable, so the oldest file is the first by name. Two exports in
      // the same second get a suffix rather than overwriting each other.
      string stamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss'Z'");
      string path = Path.Combine(Folder, $"{prefix}{stamp}.{kind}.json");
      for (int n = 2; File.Exists(path); n++)
        path = Path.Combine(Folder, $"{prefix}{stamp}-{n}.{kind}.json");

      File.WriteAllText(path, json);

      List<string> kept = [.. Directory.GetFiles(Folder, $"{prefix}*.json").Order(StringComparer.Ordinal)];
      for (int i = 0; i < kept.Count - Slots; i++)
        File.Delete(kept[i]);
    }
    catch (Exception ex)
    {
      // A failed save must never cost the user the copy itself.
      _pluginLog.Error(ex, "[ExportHistory] Failed to keep export");
    }

    return json;
  }

  /// <summary>
  /// "Kaye-Sharpedge-Cactuar" from the export itself, so the file belongs to
  /// whoever the export describes. The ledger shape carries the world under
  /// "server", the collections shape at the top. Hyphens, not spaces or "@",
  /// so the names need no quoting in a shell. Null when there is no name
  /// (logged out, or a failed build that came back as "{}").
  /// </summary>
  private static string? CharacterOf(string json)
  {
    if (JsonNode.Parse(json) is not JsonObject root) return null;

    string? name = root["name"]?.GetValue<string>();
    if (string.IsNullOrEmpty(name)) return null;

    string? world = root["server"]?["world"]?.GetValue<string>() ?? root["world"]?.GetValue<string>();
    string id = string.IsNullOrEmpty(world) ? name : $"{name} {world}";
    return id.Replace(' ', '-');
  }

  private static string SafeName(string characterId)
    => string.Join("_", characterId.Split(Path.GetInvalidFileNameChars()));
}
