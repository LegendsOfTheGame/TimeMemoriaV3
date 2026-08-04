namespace TimeMemoria.Services;

public interface IPlaytimeService : IHostedService
{
  PlaytimeRecord? Current { get; }
}

/// <summary>
/// Tracks playtime per character. Two independent figures are kept, because they
/// answer different questions and conflating them is what made the shipping
/// plugin's pacing meaningless:
///
///   LifetimePlaytime — the whole history of the character, from /playtime.
///                      Accurate but only as fresh as the last time it was run.
///   ObservedPlaytime — time this plugin has actually watched. Always current,
///                      but starts at zero the day it is installed.
///
/// No quest counting happens here. How those figures turn into a pacing number
/// is a separate decision.
/// </summary>
public class PlaytimeService(
  ILogger _logger,
  Configuration _configuration,
  IFramework _framework,
  IClientState _clientState,
  IPlayerState _playerState,
  IChatGui _chatGui) : IPlaytimeService
{
  /// <summary>Matches "Total Play Time: 36 days, 13 hours, 6 minutes".</summary>
  private static readonly Regex PlaytimePattern = new(
    @"Total Play Time:\s*(?:(\d+)\s*days?,\s*)?(?:(\d+)\s*hours?,\s*)?(?:(\d+)\s*minutes?)?",
    RegexOptions.IgnoreCase | RegexOptions.Compiled);

  /// <summary>A gap larger than this means the client was suspended, not played.</summary>
  private static readonly TimeSpan MaxTickGap = TimeSpan.FromMinutes(5);

  private static readonly TimeSpan SaveInterval = TimeSpan.FromMinutes(5);

  private string? _characterId;
  private DateTime _lastSaveUtc = DateTime.UtcNow;

  public PlaytimeRecord? Current { get; private set; }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _framework.Update += OnFrameworkUpdate;
    _clientState.Login += OnLogin;
    _clientState.Logout += OnLogout;
    _chatGui.ChatMessage += OnChatMessage;

    if (_clientState.IsLoggedIn) InitialiseCharacter();

    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    Save();

    _framework.Update -= OnFrameworkUpdate;
    _clientState.Login -= OnLogin;
    _clientState.Logout -= OnLogout;
    _chatGui.ChatMessage -= OnChatMessage;

    return _logger.ServiceLifecycle();
  }

  private void OnLogin() => InitialiseCharacter();

  private void OnLogout(int type, int code)
  {
    Save();
    Current = null;
    _characterId = null;
  }

  private void InitialiseCharacter()
  {
    if (!_playerState.IsLoaded) return;

    string world = _playerState.HomeWorld.ValueNullable?.Name.ToString() ?? "Unknown";
    _characterId = $"{_playerState.CharacterName}@{world}";

    if (_configuration.PlaytimeRecords.TryGetValue(_characterId, out PlaytimeRecord? existing))
    {
      Current = existing;
    }
    else
    {
      Current = new PlaytimeRecord { CharacterId = _characterId };
      _configuration.PlaytimeRecords[_characterId] = Current;
      Save();
    }

    Current.SessionPlaytime = TimeSpan.Zero;
    Current.LastTickUtc = DateTime.UtcNow;
  }

  private void OnFrameworkUpdate(IFramework framework)
  {
    if (Current == null || !_clientState.IsLoggedIn) return;

    DateTime now = DateTime.UtcNow;
    TimeSpan delta = now - Current.LastTickUtc;
    Current.LastTickUtc = now;

    // Discard implausible gaps rather than crediting a suspended client.
    if (delta <= TimeSpan.Zero || delta > MaxTickGap) return;

    Current.SessionPlaytime += delta;
    Current.ObservedPlaytime += delta;

    if (now - _lastSaveUtc >= SaveInterval)
    {
      Save();
      _lastSaveUtc = now;
    }
  }

  /// <summary>
  /// The game only reveals total playtime through the /playtime response, so this
  /// listens for it. The plugin never issues the command itself.
  /// </summary>
  private void OnChatMessage(IHandleableChatMessage message)
  {
    if (Current == null || message.LogKind != XivChatType.SystemMessage) return;

    Match match = PlaytimePattern.Match(message.Message.ToString());
    if (!match.Success) return;

    int days = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
    int hours = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
    int minutes = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
    if (days == 0 && hours == 0 && minutes == 0) return;

    Current.LifetimePlaytime = TimeSpan.FromDays(days) + TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes);
    Current.LifetimePlaytimeUpdatedUtc = DateTime.UtcNow;
    Save();

    _logger.Debug($"[Playtime] Lifetime playtime updated to {Current.LifetimePlaytime}");
  }

  private void Save()
  {
    if (_characterId == null || Current == null) return;
    _configuration.PlaytimeRecords[_characterId] = Current;
    _configuration.Save();
  }
}
