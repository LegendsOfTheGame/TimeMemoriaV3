using AchievementRow = Lumina.Excel.Sheets.Achievement;
using AchievementState = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;

namespace TimeMemoria.Services;

/// <summary>
/// A reading taken from one tier of an achievement series.
/// </summary>
/// <param name="Value">
/// The number the client reported. Exact when the tier is still in progress; a
/// floor when it is complete, because a finished tier reports its own
/// requirement rather than the running total.
/// </param>
/// <param name="IsExact">False when <paramref name="Value"/> is only a floor.</param>
/// <param name="Tier">1-based position in the series, used to prefer later readings.</param>
/// <param name="TierCount">How many tiers the series has, so "5 of 7" can be said.</param>
/// <param name="TakenUtc">When it was seen. The figure is exactly this old.</param>
public record AchievementReading(int Value, bool IsExact, int Tier, int TierCount, DateTime TakenUtc);

public interface IAchievementService : IHostedService
{
  /// <summary>Collectables gathered or caught, if the client has ever reported it.</summary>
  AchievementReading? Gathered { get; }

  /// <summary>Collectables synthesised, if the client has ever reported it.</summary>
  AchievementReading? Crafted { get; }

  /// <summary>
  /// Instanced dungeons, raids and trials completed, if the client has ever
  /// reported it. A lifetime completion count, which is a progression figure in
  /// the same family as commendations — nothing about how any duty went.
  /// </summary>
  AchievementReading? Duties { get; }
}

/// <summary>
/// How many collectables this character has gathered and crafted.
///
/// The game holds no running counter for either — the count lives in an
/// achievement, server-side, and the client fetches it only for whichever
/// achievement you are looking at. There is a call that would request it, and
/// this deliberately does not use it: the plugin does not talk to the game
/// server on your behalf, for the same reason it will not run /playtime for you.
///
/// So this watches instead. Open the Achievements window, look at a tier, and
/// the number lands in a slot this reads. Nothing is asked for; what the game
/// fetched because you looked is simply not thrown away.
///
/// The reading is kept from the highest tier seen, since later tiers count
/// further. A tier already completed reports its own requirement rather than the
/// real total, so those are recorded as a floor and said so.
/// </summary>
public class AchievementService(ILogger _logger, IFramework _framework, IDataManager _dataManager,
  IClientState _clientState, IPlayerState _playerState, Configuration _configuration) : IAchievementService
{
  private enum Series { Gathered, Crafted, Duties }

  /// <summary>
  /// Each series as the name stems its tiers are called, rather than as ids, so a
  /// tier added in a future patch is picked up without anything here changing.
  ///
  /// Two of these are a single stem numbered upward. The duty one is not: it runs
  /// Dungeon Siege I-IV, then Dungeon Master at a thousand, then Lifer I-III to
  /// ten thousand — three unrelated names for one counter, which is why a series
  /// is a list of stems and not one string.
  /// </summary>
  private static readonly (Series Series, string[] Stems)[] SeriesStems =
  [
    (Series.Gathered, ["I Collected That"]),
    (Series.Crafted, ["I Made That (Worth Collecting)"]),
    (Series.Duties, ["Dungeon Siege", "Dungeon Master", "Lifer"])
  ];

  private readonly Dictionary<uint, (int Tier, int Count, Series Series)> _tiers = [];

  private uint _lastId;
  private uint _lastValue;

  public AchievementReading? Gathered { get; private set; }

  public AchievementReading? Crafted { get; private set; }

  public AchievementReading? Duties { get; private set; }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    BuildSeries();

    _framework.Update += OnFrameworkUpdate;
    _clientState.Login += OnLogin;

    Restore();

    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _framework.Update -= OnFrameworkUpdate;
    _clientState.Login -= OnLogin;

    return _logger.ServiceLifecycle();
  }

  /// <summary>
  /// Indexes every series by achievement id, recording each tier's position.
  ///
  /// Ordered by row id, which ascends with the tier because these series were
  /// extended patch by patch and later tiers therefore came later. That holds
  /// across the duty series' three name stems as well: Dungeon Siege shipped at
  /// launch and the Lifer tiers long after.
  ///
  /// The tier counts are logged because a stem matching more or fewer rows than
  /// expected is the one failure mode here, and it would otherwise be silent —
  /// a series that matches nothing simply never reports.
  /// </summary>
  private void BuildSeries()
  {
    _tiers.Clear();

    foreach ((Series series, string[] stems) in SeriesStems)
    {
      List<uint> ids =
      [
        .. _dataManager.GetExcelSheet<AchievementRow>()
          .Where((r) => stems.Any((stem) => r.Name.ToString().StartsWith(stem, StringComparison.OrdinalIgnoreCase)))
          .Select((r) => r.RowId)
          .Order()
      ];

      for (int i = 0; i < ids.Count; i++) _tiers[ids[i]] = (i + 1, ids.Count, series);

      _logger.Debug($"[Achievement] {series}: {ids.Count} tiers from {string.Join(", ", stems)}.");
    }
  }

  private void OnLogin()
  {
    Gathered = null;
    Crafted = null;
    Duties = null;

    _lastId = 0;
    _lastValue = 0;

    Restore();
  }

  /// <summary>
  /// Watches the single progress slot. It holds only the most recently fetched
  /// achievement, so sampling it on demand catches whatever happened to be there
  /// last — which is how asking about gathering returns a crafting number.
  /// </summary>
  private unsafe void OnFrameworkUpdate(IFramework framework)
  {
    AchievementState* state = AchievementState.Instance();
    if (state is null || !state->IsLoaded()) return;

    uint id = state->ProgressAchievementId;
    uint value = state->ProgressCurrent;

    if (id == _lastId && value == _lastValue) return;

    _lastId = id;
    _lastValue = value;

    if (!_tiers.TryGetValue(id, out (int Tier, int Count, Series Series) tier)) return;

    // A completed tier reports its own requirement rather than the running
    // total, so its number is a floor. Completion says this on its own — there
    // is no need to look the requirement up and compare against it.
    bool complete = state->IsComplete((int)id);

    AchievementReading reading = new((int)value, !complete, tier.Tier, tier.Count, DateTime.UtcNow);

    switch (tier.Series)
    {
      case Series.Gathered: Gathered = Prefer(Gathered, reading); break;
      case Series.Crafted: Crafted = Prefer(Crafted, reading); break;
      case Series.Duties: Duties = Prefer(Duties, reading); break;
    }

    Persist();

    _logger.Debug($"[Achievement] {tier.Series} tier {tier.Tier} of {tier.Count}: " +
                  $"{value:N0}{(complete ? " (floor)" : "")}");
  }

  /// <summary>
  /// Later tiers count further, so a reading from a higher tier replaces a
  /// lower one. At the same tier the newer reading wins.
  /// </summary>
  private static AchievementReading Prefer(AchievementReading? existing, AchievementReading candidate)
    => existing is null || candidate.Tier > existing.Tier
       || (candidate.Tier == existing.Tier && candidate.TakenUtc > existing.TakenUtc)
      ? candidate
      : existing;

  /// <summary>Keyed exactly as playtime is, so both follow the same character.</summary>
  private string? Key
  {
    get
    {
      if (!_playerState.IsLoaded) return null;

      string world = _playerState.HomeWorld.ValueNullable?.Name.ToString() ?? "Unknown";
      return $"{_playerState.CharacterName}@{world}";
    }
  }

  private void Restore()
  {
    if (Key is not { } key) return;
    if (!_configuration.AchievementReadings.TryGetValue(key, out StoredReadings? stored)) return;

    Gathered = stored.Gathered;
    Crafted = stored.Crafted;
    Duties = stored.Duties;
  }

  private void Persist()
  {
    if (Key is not { } key) return;

    _configuration.AchievementReadings[key] =
      new StoredReadings { Gathered = Gathered, Crafted = Crafted, Duties = Duties };
    _configuration.Save();
  }
}

/// <summary>
/// Every series' latest reading for one character.
///
/// Properties are only ever added here. An older configuration deserialises with
/// the new ones null, which reads as "never seen" and is exactly right — that
/// character genuinely has no reading for a series the plugin could not yet take.
/// </summary>
[Serializable]
public class StoredReadings
{
  public AchievementReading? Gathered { get; set; }
  public AchievementReading? Crafted { get; set; }
  public AchievementReading? Duties { get; set; }
}
