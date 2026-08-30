using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using ClassRow = Lumina.Excel.Sheets.WKSCosmoToolClass;

namespace TimeMemoria.Services;

/// <summary>
/// One data type's banked research progress toward a character's Cosmic Tool.
/// </summary>
[Serializable]
public class CosmicResearchProgress
{
  /// <summary>1-indexed: Type I is 1, not 0 — the module's own convention.</summary>
  public byte Type { get; set; }

  public ushort Current { get; set; }
  public ushort Needed { get; set; }
  public ushort Max { get; set; }
}

[Serializable]
public class CosmicToolProgress
{
  public string Job { get; set; } = "";
  public List<CosmicResearchProgress> Types { get; set; } = [];
}

/// <summary>
/// A full pass over every class, taken in one moment. Kept as a batch rather
/// than a reading per class because that is how it is actually read — one
/// WKSManager instance answers every class in the same framework tick, so
/// there is exactly one age worth recording, not eleven.
/// </summary>
[Serializable]
public class CosmicToolReading
{
  public List<CosmicToolProgress> Jobs { get; set; } = [];
  public DateTime TakenUtc { get; set; }
}

public interface ICosmicToolProgressService : IHostedService
{
  /// <summary>
  /// Every class with at least one point of banked research data, each carrying
  /// only the data types its current stage actually asks for, as of
  /// <see cref="TakenUtc"/>.
  ///
  /// Live whenever WKSManager can be read (inside a Cosmic Exploration zone);
  /// otherwise the last reading this character ever produced, persisted across
  /// zone changes, logout and plugin restarts. Empty only for a character that
  /// has never once been somewhere the module could be read.
  /// </summary>
  List<CosmicToolProgress> GetProgress();

  /// <summary>When <see cref="GetProgress"/>'s data was actually read. Null if never.</summary>
  DateTime? TakenUtc { get; }
}

/// <summary>
/// Cosmic Tool research data, per class — the number behind a Stellar Mission's
/// "You submitted N points toward the '&lt;class&gt; &lt;type&gt;' dataset" chat line.
///
/// Confirmed against VanillaPlus's shipped source (MidoriKami,
/// github.com/MidoriKami/VanillaPlus, AGPL-3.0) rather than reverse-engineered
/// from a struct dump: <c>WKSManager.Instance()->ResearchModule</c> is the live
/// module, and <c>GetCurrentAnalysis</c>/<c>GetNeededAnalysis</c>/
/// <c>GetMaxAnalysis</c> take <c>(jobId, researchType)</c> directly. jobId is the
/// <see cref="ClassRow"/> row id as-is (Goldsmith = 4); researchType is
/// 1-indexed, one past the <c>Types</c> collection's own array index.
///
/// Every class returns *something* even before its Cosmic Tool prototype has
/// ever been picked up — research data can be earned by missions without the
/// tool equipped, so the module always reports the first stage's requirement
/// (needed=200, max=500, current=0) as a live, meaningful zero. Filtering here
/// on Current > 0 is therefore load-bearing, not cosmetic: without it, every
/// export would carry all eleven classes at zero forever, and the day a class
/// legitimately starts at zero-with-real-progress would be indistinguishable
/// from the day nobody had touched it.
///
/// <c>ResearchModule</c> is only non-null while physically inside a Cosmic
/// Exploration zone — confirmed live 29/08/2026, present at Sinus Ardorum and
/// null immediately after teleporting to Limsa Lominsa. Reading it live and
/// nothing else would make an export's cosmicTools field flicker in and out
/// purely on where the character happened to stand, which is worse than
/// useless for a value someone pastes somewhere and keeps — so this behaves
/// exactly like <see cref="AchievementService"/>'s collectable counts: watch
/// while it is readable, persist what was seen, and answer from the record the
/// rest of the time.
/// </summary>
public unsafe class CosmicToolProgressService(
  ILogger _logger,
  Configuration _configuration,
  IFramework _framework,
  IClientState _clientState,
  IPlayerState _playerState,
  IDataManager _dataManager) : ICosmicToolProgressService
{
  private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

  private CosmicToolReading? _current;
  private DateTime _lastPollUtc = DateTime.MinValue;

  public List<CosmicToolProgress> GetProgress() => _current?.Jobs ?? [];

  public DateTime? TakenUtc => _current?.TakenUtc;

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _framework.Update += OnFrameworkUpdate;
    _clientState.Login += OnLogin;

    if (_clientState.IsLoggedIn) OnLogin();

    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _framework.Update -= OnFrameworkUpdate;
    _clientState.Login -= OnLogin;

    return _logger.ServiceLifecycle();
  }

  private void OnLogin()
  {
    _current = null;

    if (Key is not { } key) return;
    if (_configuration.CosmicToolReadings.TryGetValue(key, out CosmicToolReading? stored)) _current = stored;
  }

  private void OnFrameworkUpdate(IFramework framework)
  {
    DateTime now = DateTime.UtcNow;
    if (now - _lastPollUtc < PollInterval) return;
    _lastPollUtc = now;

    if (!_playerState.IsLoaded) return;

    WKSManager* wks = WKSManager.Instance();
    if (wks is null || !wks->IsLoaded) return;

    WKSResearchModule* research = wks->ResearchModule;
    if (research is null || !research->IsLoaded) return;

    List<CosmicToolProgress> jobs = ReadAllClasses(research);
    if (jobs.Count == 0) return;

    _current = new CosmicToolReading { Jobs = jobs, TakenUtc = now };
    Persist();
  }

  private List<CosmicToolProgress> ReadAllClasses(WKSResearchModule* research)
  {
    List<CosmicToolProgress> result = [];
    Lumina.Excel.ExcelSheet<ClassJob> jobSheet = _dataManager.GetExcelSheet<ClassJob>();

    foreach (ClassRow row in _dataManager.GetExcelSheet<ClassRow>())
    {
      if (row.RowId == 0) continue;

      byte jobId = (byte)row.RowId;
      List<CosmicResearchProgress> types = [];

      for (int i = 0; i < row.Types.Count; i++)
      {
        byte researchType = (byte)(i + 1);

        ushort current = research->GetCurrentAnalysis(jobId, researchType);
        ushort needed = research->GetNeededAnalysis(jobId, researchType);
        ushort max = research->GetMaxAnalysis(jobId, researchType);

        if (needed == 0) continue;

        types.Add(new CosmicResearchProgress { Type = researchType, Current = current, Needed = needed, Max = max });
      }

      if (types.All((t) => t.Current == 0)) continue;

      // The real ClassJob sheet id is the WKSCosmoToolClass row id plus 7 —
      // VanillaPlus's own derivation, run in reverse. Not guessed: the plugin's
      // ClassJobProgressService names crafters and gatherers 8-18 the same way.
      string jobName = jobSheet.GetRowOrDefault(jobId + 7u) is { } job
        ? ClassJobProgressService.ToDisplayName(job.Name.ToString())
        : $"job{jobId}";

      result.Add(new CosmicToolProgress { Job = jobName, Types = types });
    }

    return result;
  }

  /// <summary>Keyed exactly as playtime and achievement readings are.</summary>
  private string? Key
  {
    get
    {
      if (!_playerState.IsLoaded) return null;

      string world = _playerState.HomeWorld.ValueNullable?.Name.ToString() ?? "Unknown";
      return $"{_playerState.CharacterName}@{world}";
    }
  }

  private void Persist()
  {
    if (Key is not { } key || _current is null) return;

    _configuration.CosmicToolReadings[key] = _current;
    _configuration.Save();
  }
}
