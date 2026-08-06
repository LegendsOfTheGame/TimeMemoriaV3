namespace TimeMemoria.Services;

/// <summary>
/// A single class or job's current progression: level, experience earned toward
/// the next level, and what that level requires in total. Read live from game
/// memory — nothing here is stored or accumulated.
/// </summary>
public class ClassJobProgress
{
  public required string Name { get; init; }
  public required string Abbreviation { get; init; }
  public required string Category { get; init; }

  /// <summary>
  /// The base class this job grew out of — Marauder for Warrior, Gladiator for
  /// Paladin — or null where there is none.
  ///
  /// The two share a level, so a level 20 "Warrior" is really still a Marauder,
  /// and everything outstanding for them is filed in the journal under the class
  /// name. Anything searching by name needs both.
  /// </summary>
  public string? ClassName { get; init; }

  /// <summary>Tank, Healer, DPS, Crafter or Gatherer.</summary>
  public required string Role { get; init; }
  public int Level { get; init; }
  public int Experience { get; init; }

  /// <summary>Total experience the current level requires. Zero at the ceiling.</summary>
  public int ExperienceToNext { get; init; }

  /// <summary>Blue Mage and friends, which cap below the normal ceiling.</summary>
  public bool IsLimitedJob { get; init; }

  public bool IsUnlocked => Level > 0;
  public bool IsMaxLevel => IsUnlocked && ExperienceToNext == 0;

  /// <summary>Progress through the current level, 0..1. Full at max.</summary>
  public float Fraction =>
    ExperienceToNext > 0 ? Math.Clamp(Experience / (float)ExperienceToNext, 0f, 1f) : 1f;
}

public interface IClassJobProgressService
{
  List<ClassJobProgress> GetProgress();
}

/// <summary>
/// Reports class and job levels with experience progress, read through Dalamud's
/// IPlayerState. Holds no state of its own beyond a cached list of which sheet
/// rows are worth reading.
/// </summary>
public class ClassJobProgressService(IPluginLog _pluginLog, IPlayerState _playerState, IDataManager _dataManager)
  : IClassJobProgressService
{
  /// <summary>
  /// One representative ClassJob row per experience slot. The sheet lists both a
  /// base class and its job (Gladiator and Paladin) sharing one ExpArrayIndex, so
  /// only jobs are kept where a slot has them. Arcanist is the exception —
  /// Summoner and Scholar both sit on it and the game lists both.
  /// </summary>
  private List<ClassJob>? _trackedJobs;

  public List<ClassJobProgress> GetProgress()
  {
    if (!_playerState.IsLoaded) return [];

    try
    {
      Lumina.Excel.ExcelSheet<ParamGrow> paramGrow = _dataManager.GetExcelSheet<ParamGrow>();
      List<ClassJobProgress> result = [];

      // The character's level ceiling, which accounts for owned expansions.
      unsafe
      {
        FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState* state =
          FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
        int maxLevel = state != null ? state->MaxLevel : 0;

        foreach (ClassJob job in GetTrackedJobs())
        {
          int level = _playerState.GetClassJobLevel(job);
          int exp = _playerState.GetClassJobExperience(job);

          // ParamGrow keeps returning a requirement at the ceiling rather than
          // zero, so max has to be detected from MaxLevel instead.
          bool isLimited = job.IsLimitedJob;
          bool atCeiling = !isLimited && maxLevel > 0 && level >= maxLevel;
          int toNext = 0;

          if (level > 0 && !atCeiling)
          {
            ParamGrow? row = paramGrow.GetRowOrDefault((uint)level);
            if (row.HasValue) toNext = row.Value.ExpToNext;
          }

          // Null when the parent is the job itself, which is how the sheet
          // represents a job that was never a class — Reaper, Sage and so on.
          string jobName = ToDisplayName(job.Name.ToString());
          string? className = job.ClassJobParent.ValueNullable is { } parent && parent.RowId != job.RowId
            ? ToDisplayName(parent.Name.ToString())
            : null;

          result.Add(new ClassJobProgress
          {
            Name = jobName,
            ClassName = string.Equals(className, jobName, StringComparison.OrdinalIgnoreCase) ? null : className,
            Abbreviation = job.Abbreviation.ToString(),
            Category = GetCategory(job),
            Role = GetRole(job),
            Level = level,
            Experience = atCeiling ? 0 : exp,
            ExperienceToNext = toNext,
            IsLimitedJob = isLimited
          });
        }
      }

      return result;
    }
    catch (Exception ex)
    {
      _pluginLog.Error(ex, "[ClassJobProgress] Failed to read class/job progression");
      return [];
    }
  }

  /// <summary>
  /// Combat jobs carry a non-zero Role. Hand and land both sit at Role 0, and
  /// DohDolJobIndex cannot separate them — it numbers within each group, so
  /// crafters run 0-7 and gatherers 0-2. Row IDs do separate them and are stable
  /// across languages: 8-15 are Carpenter through Culinarian, 16-18 the gatherers.
  /// </summary>
  private static string GetCategory(ClassJob job)
  {
    if (job.Role != 0) return "combat";

    return job.RowId switch
    {
      >= 16 and <= 18 => "gather",
      >= 8 and <= 15 => "craft",
      _ => "combat"
    };
  }

  /// <summary>
  /// The sheet's Role column separates tanks, healers and the two DPS kinds;
  /// melee and ranged are merged, since for levelling purposes the distinction
  /// does not change what you would pick next.
  /// </summary>
  private static string GetRole(ClassJob job) => job.Role switch
  {
    1 => "Tank",
    4 => "Healer",
    2 or 3 => "DPS",
    _ => GetCategory(job) == "gather" ? "Gatherer" : "Crafter"
  };

  /// <summary>
  /// The sheet stores names lowercase ("white mage") while the game presents them
  /// capitalised. Only re-cases values that arrive entirely lowercase, so
  /// localisations with their own casing rules are left alone.
  /// </summary>
  public static string ToDisplayName(string name)
  {
    if (name.Length == 0 || name.Any(char.IsUpper)) return name;
    return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
  }

  private List<ClassJob> GetTrackedJobs()
  {
    if (_trackedJobs != null) return _trackedJobs;

    _trackedJobs = _dataManager.GetExcelSheet<ClassJob>()
      .Where(job => job.ExpArrayIndex >= 0 && !job.Name.IsEmpty)
      .GroupBy(job => job.ExpArrayIndex)
      .SelectMany(group =>
      {
        List<ClassJob> jobs = [.. group.Where(job => job.JobIndex > 0).OrderBy(job => job.RowId)];
        return jobs.Count > 0 ? jobs : group.OrderBy(job => job.RowId).Take(1);
      })
      // UIPriority is the sheet's own display order — the same one the in-game
      // Character panel sorts by.
      .OrderBy(job => job.UIPriority)
      .ThenBy(job => job.ExpArrayIndex)
      .ToList();

    _pluginLog.Debug($"[ClassJobProgress] Tracking {_trackedJobs.Count} class/job slots");
    return _trackedJobs;
  }
}
