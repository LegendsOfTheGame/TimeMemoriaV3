namespace TimeMemoria.Services;

public interface INewsService : IHostedService
{
  NewsEvent? Latest { get; }
  string? FetchError { get; }
  bool IsLoading { get; }

  /// <summary>Called from the draw loop. Never blocks; never throws.</summary>
  void Poll();
}

/// <summary>
/// Fetches the world-state feed. Read-only and outbound-only in the sense that
/// it requests a public file — no player data ever leaves the machine.
/// </summary>
public class NewsService(ILogger _logger) : INewsService
{
  private const string LatestNewsUrl =
    "https://raw.githubusercontent.com/LegendsOfTheGame/ffxiv-latest-news/main/LatestNews.json";

  /// <summary>
  /// The feed's cron runs every 6 hours from 00:00 UTC (update_news.yml). The
  /// file only changes after a run, so polling harder between runs just spends
  /// GitHub's rate limit. Schedule suggested by new Era on the Dalamud Discord,
  /// 27 Oct 2026.
  /// </summary>
  private const int FeedCronHours = 6;

  /// <summary>How long after each cron slot counts as "near" a run.</summary>
  private static readonly TimeSpan NearWindow = TimeSpan.FromHours(1);

  private static readonly TimeSpan NearInterval = TimeSpan.FromMinutes(15);
  private static readonly TimeSpan FarInterval = TimeSpan.FromMinutes(30);

  /// <summary>
  /// Random spread on every interval, so installs drift apart instead of all
  /// hitting GitHub in the same minute.
  /// </summary>
  private static readonly TimeSpan Jitter = TimeSpan.FromMinutes(3);

  /// <summary>First retry delay after a failure; doubles per failure.</summary>
  private static readonly TimeSpan BackoffBase = TimeSpan.FromSeconds(60);

  /// <summary>Ceiling for the failure backoff, so a dead endpoint is retried rarely.</summary>
  private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

  private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
  private readonly CancellationTokenSource _cts = new();

  private NewsEvent? _cached;
  private string? _fetchError;
  private volatile bool _isFetching;

  /// <summary>
  /// When the next attempt is allowed. Advanced on *every* outcome — advancing it
  /// only on success would let a failing endpoint be retried once per frame.
  /// </summary>
  private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;
  private int _consecutiveFailures;

  public NewsEvent? Latest => _cached;
  public string? FetchError => _fetchError;
  public bool IsLoading => _isFetching;

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _ = RefreshAsync();
    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _cts.Cancel();
    _cts.Dispose();
    _http.Dispose();
    return _logger.ServiceLifecycle();
  }

  public void Poll()
  {
    if (_isFetching) return;
    if (DateTimeOffset.UtcNow < _nextAttempt) return;
    _ = RefreshAsync();
  }

  private async Task RefreshAsync()
  {
    _isFetching = true;

    try
    {
      // Cache-buster: raw.githubusercontent.com caches for several minutes, which
      // would otherwise make a fresh fetch return a stale maintenance window.
      string url = $"{LatestNewsUrl}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
      string json = await _http.GetStringAsync(url, _cts.Token).ConfigureAwait(false);

      _cached = JsonSerializer.Deserialize<NewsEvent>(json, new JsonSerializerOptions
      {
        PropertyNameCaseInsensitive = true
      });

      _fetchError = null;
      _consecutiveFailures = 0;
      _nextAttempt = DateTimeOffset.UtcNow + NextInterval(DateTimeOffset.UtcNow);
      _logger.Debug("[NewsService] LatestNews.json refreshed.");
    }
    catch (OperationCanceledException)
    {
      // Shutting down.
    }
    catch (Exception ex)
    {
      _fetchError = ex.Message;
      _consecutiveFailures++;

      // Exponential backoff, capped. Without this the draw loop would retry
      // continuously for as long as the panel stayed open.
      double seconds = BackoffBase.TotalSeconds * Math.Pow(2, Math.Min(_consecutiveFailures, 8));
      TimeSpan backoff = TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
      _nextAttempt = DateTimeOffset.UtcNow + backoff;

      _logger.Debug($"[NewsService] Fetch failed ({_consecutiveFailures}), retrying in {backoff.TotalSeconds:N0}s: {ex.Message}");
    }
    finally
    {
      _isFetching = false;
    }
  }

  /// <summary>
  /// 15 minutes in the hour after a cron slot, 30 otherwise, each ±3 minutes.
  /// GitHub often starts scheduled runs late, so a late file is still picked
  /// up by the 30-minute cadence rather than missed until the next slot.
  /// </summary>
  private static TimeSpan NextInterval(DateTimeOffset nowUtc)
  {
    TimeSpan sinceSlot = TimeSpan.FromHours(nowUtc.UtcDateTime.TimeOfDay.TotalHours % FeedCronHours);
    TimeSpan interval = sinceSlot < NearWindow ? NearInterval : FarInterval;
    double jitterSeconds = (Random.Shared.NextDouble() * 2 - 1) * Jitter.TotalSeconds;
    return interval + TimeSpan.FromSeconds(jitterSeconds);
  }
}
