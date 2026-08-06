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
  /// <summary>
  /// Series are found by name rather than by id, so a tier added in a future
  /// patch is picked up without anything here changing.
  /// </summary>
  private const string GatheredSeries = "I Collected That";

  private const string CraftedSeries = "I Made That (Worth Collecting)";

  private readonly Dictionary<uint, (int Tier, int Count, bool Gathered)> _tiers = [];

  private uint _lastId;
  private uint _lastValue;

  public AchievementReading? Gathered { get; private set; }

  public AchievementReading? Crafted { get; private set; }

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
  /// Indexes both series by achievement id, recording each tier's position.
  ///
  /// Ordered by row id, which ascends with the tier because the series has been
  /// extended patch by patch. Found by name rather than by hardcoded id, so a
  /// tier added later is picked up with no change here.
  /// </summary>
  private void BuildSeries()
  {
    _tiers.Clear();

    foreach ((string prefix, bool gathered) in new[] { (GatheredSeries, true), (CraftedSeries, false) })
    {
      List<uint> ids =
      [
        .. _dataManager.GetExcelSheet<AchievementRow>()
          .Where((r) => r.Name.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
          .Select((r) => r.RowId)
          .Order()
      ];

      for (int i = 0; i < ids.Count; i++) _tiers[ids[i]] = (i + 1, ids.Count, gathered);

      _logger.Debug($"[Achievement] '{prefix}': {ids.Count} tiers.");
    }
  }

  private void OnLogin()
  {
    Gathered = null;
    Crafted = null;

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

    if (!_tiers.TryGetValue(id, out (int Tier, int Count, bool Gathered) tier)) return;

    // A completed tier reports its own requirement rather than the running
    // total, so its number is a floor. Completion says this on its own — there
    // is no need to look the requirement up and compare against it.
    bool complete = state->IsComplete((int)id);

    AchievementReading reading = new((int)value, !complete, tier.Tier, tier.Count, DateTime.UtcNow);

    if (tier.Gathered) Gathered = Prefer(Gathered, reading);
    else Crafted = Prefer(Crafted, reading);

    Persist();

    _logger.Debug($"[Achievement] tier {tier.Tier} of {(tier.Gathered ? "gathered" : "crafted")}: " +
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
  }

  private void Persist()
  {
    if (Key is not { } key) return;

    _configuration.AchievementReadings[key] = new StoredReadings { Gathered = Gathered, Crafted = Crafted };
    _configuration.Save();
  }
}

/// <summary>Both series' latest readings for one character.</summary>
[Serializable]
public class StoredReadings
{
  public AchievementReading? Gathered { get; set; }
  public AchievementReading? Crafted { get; set; }
}
