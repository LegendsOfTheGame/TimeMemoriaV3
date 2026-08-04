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

  /// <summary>Matches the cadence of the source page this was modelled on.</summary>
  private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

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
      _nextAttempt = DateTimeOffset.UtcNow + RefreshInterval;
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
      double seconds = RefreshInterval.TotalSeconds * Math.Pow(2, Math.Min(_consecutiveFailures, 8));
      TimeSpan backoff = TimeSpan.FromSeconds(Math.Min(seconds, MaxBackoff.TotalSeconds));
      _nextAttempt = DateTimeOffset.UtcNow + backoff;

      _logger.Debug($"[NewsService] Fetch failed ({_consecutiveFailures}), retrying in {backoff.TotalSeconds:N0}s: {ex.Message}");
    }
    finally
    {
      _isFetching = false;
    }
  }
}
