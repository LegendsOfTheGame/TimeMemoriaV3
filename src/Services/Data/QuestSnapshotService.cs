namespace TimeMemoria.Services;

/// <summary>A quest first seen after the snapshot was taken.</summary>
public record NewQuest(uint Id, string Title, string Section, string Expansion, int Level, string SeenOn, string GameVersion, uint FestivalId = 0);

/// <summary>
/// The quest set as it stood at a point in time, with the game build it came
/// from. Not per character -- what content exists is a property of the game.
/// </summary>
public class QuestSnapshot
{
  public string GameVersion { get; set; } = "";
  public string TakenOn { get; set; } = "";
  public List<uint> QuestIds { get; set; } = [];
  public List<NewQuest> Additions { get; set; } = [];
}

public interface IQuestSnapshotService : IHostedService
{
  /// <summary>Quests that have appeared since the first snapshot, newest first.</summary>
  IReadOnlyList<NewQuest> Additions { get; }

  /// <summary>The game build the snapshot was last reconciled against.</summary>
  string GameVersion { get; }

  /// <summary>When the baseline was first taken.</summary>
  string BaselineDate { get; }

  /// <summary>Total quests in the current game data.</summary>
  int KnownQuests { get; }
}

/// <summary>
/// Notices when a patch adds quests.
///
/// Nothing in the game files records which patch a quest belongs to, so the only
/// way to know something is new is to have seen what came before. This keeps
/// that record: the full set of quest ids and the game build they were read
/// from, compared on every load.
///
/// The first run establishes a baseline and reports nothing, because everything
/// is new the first time you look. From then on, any id absent from the
/// baseline genuinely appeared afterwards.
/// </summary>
public class QuestSnapshotService(
  ILogger _logger,
  IDalamudPluginInterface _pluginInterface,
  IDataManager _dataManager,
  IFramework _framework,
  IDataService _dataService) : IQuestSnapshotService
{
  private const string FileName = "quest-snapshot.json";

  private QuestSnapshot? _snapshot;
  private bool _reconciled;

  public IReadOnlyList<NewQuest> Additions => _snapshot?.Additions ?? [];
  public string GameVersion => _snapshot?.GameVersion ?? "";
  public string BaselineDate => _snapshot?.TakenOn ?? "";
  public int KnownQuests => _snapshot?.QuestIds.Count ?? 0;

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _framework.Update += OnFrameworkUpdate;
    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _framework.Update -= OnFrameworkUpdate;
    return _logger.ServiceLifecycle();
  }

  /// <summary>
  /// Runs once per session, as soon as the quest tree has actually been built.
  /// Reconciling against an empty tree would record every quest in the game as
  /// having vanished and then returned.
  /// </summary>
  private void OnFrameworkUpdate(IFramework framework)
  {
    if (_reconciled) return;

    // The raw tree, not the filtered one. QuestData has this character's
    // unavailable quests stripped out of it -- other starting cities, other
    // Grand Companies -- so reconciling against it would make the set depend on
    // who is logged in, and a later read of the unfiltered tree would then
    // report those quests as newly added.
    List<uint> current = [];
    Collect(_dataService.RawQuestData, current);
    if (current.Count == 0) return;

    _reconciled = true;

    try
    {
      Reconcile(current);
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "[Snapshot] Reconcile failed");
    }
  }

  private void Reconcile(List<uint> current)
  {
    string version = CurrentGameVersion();
    _snapshot = Load();

    if (_snapshot is null)
    {
      _snapshot = new QuestSnapshot
      {
        GameVersion = version,
        TakenOn = DateTime.Now.ToString("yyyy-MM-dd"),
        QuestIds = [.. current.Order()]
      };

      Save();
      _logger.Debug($"[Snapshot] Baseline taken: {current.Count} quests at game build {version}.");
      return;
    }

    HashSet<uint> known = [.. _snapshot.QuestIds];
    List<uint> added = [.. current.Where((id) => !known.Contains(id))];

    if (added.Count == 0)
    {
      // Still worth recording the build, so the panel can say what it last saw.
      if (_snapshot.GameVersion != version)
      {
        _snapshot.GameVersion = version;
        Save();
      }

      return;
    }

    string today = DateTime.Now.ToString("yyyy-MM-dd");
    Dictionary<uint, NewQuest> details = Describe(added, today, version);

    foreach (uint id in added)
      if (details.TryGetValue(id, out NewQuest? quest))
        _snapshot.Additions.Insert(0, quest);

    _snapshot.QuestIds = [.. current.Order()];
    _snapshot.GameVersion = version;
    Save();

    _logger.Debug($"[Snapshot] {added.Count} new quest(s) at game build {version}.");
  }

  /// <summary>Walks the tree once to attach titles and locations to the new ids.</summary>
  private Dictionary<uint, NewQuest> Describe(List<uint> ids, string date, string version)
  {
    HashSet<uint> wanted = [.. ids];
    Dictionary<uint, NewQuest> found = [];

    void Walk(QuestData node, string expansion, string section)
    {
      foreach (Types.Quest quest in node.Quests)
      {
        if (quest.Ids.Count == 0) continue;
        uint id = quest.Ids[0];
        if (!wanted.Contains(id) || found.ContainsKey(id)) continue;

        found[id] = new NewQuest(id, quest.Title, quest.Section, expansion, quest.Level, date, version, quest.FestivalId);
      }

      foreach (QuestData child in node.Categories)
        Walk(child, expansion.Length == 0 ? child.Title : expansion, section);
    }

    foreach (QuestData expansion in _dataService.RawQuestData.Categories)
      Walk(expansion, expansion.Title, "");

    return found;
  }

  private static void Collect(QuestData node, List<uint> into)
  {
    foreach (Types.Quest quest in node.Quests)
      if (quest.Ids.Count > 0) into.Add(quest.Ids[0]);

    foreach (QuestData child in node.Categories)
      Collect(child, into);
  }

  private string CurrentGameVersion()
  {
    try
    {
      return _dataManager.GameData.Repositories.TryGetValue("ffxiv", out var repo)
        ? repo.Version ?? "unknown"
        : "unknown";
    }
    catch
    {
      return "unknown";
    }
  }

  private string Path_ => Path.Combine(_pluginInterface.ConfigDirectory.FullName, FileName);

  private QuestSnapshot? Load()
  {
    try
    {
      _pluginInterface.ConfigDirectory.Create();
      return File.Exists(Path_)
        ? JsonSerializer.Deserialize<QuestSnapshot>(File.ReadAllText(Path_))
        : null;
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "[Snapshot] Failed to load");
      return null;
    }
  }

  private void Save()
  {
    if (_snapshot is null) return;

    try
    {
      _pluginInterface.ConfigDirectory.Create();
      File.WriteAllText(Path_, JsonSerializer.Serialize(_snapshot, new JsonSerializerOptions { WriteIndented = true }));
    }
    catch (Exception ex)
    {
      _logger.Error(ex, "[Snapshot] Failed to save");
    }
  }
}
