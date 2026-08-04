namespace TimeMemoria.Types;

/// <summary>
/// One snapshot of LatestNews.json. Timestamps are UTC Unix seconds and are
/// converted to local time only at the point of display.
/// </summary>
public class NewsEvent
{
  public MaintenanceWindow? Maintenance { get; init; }
  public MaintenanceWindow? LastMaintenance { get; init; }

  public GameEvent[] Events { get; init; } = [];
  public GameEvent? LastEvent { get; init; }

  public string? Version { get; init; }
  public long LastUpdated { get; init; }
  public string? Source { get; init; }
}

public class MaintenanceWindow
{
  public string? Title { get; init; }
  public long? Start { get; init; }
  public long? End { get; init; }
  public long? Time { get; init; }
  public string? Url { get; init; }
}

public class GameEvent
{
  public string? Title { get; init; }
  public long? Start { get; init; }
  public long? End { get; init; }
  public string? Url { get; init; }
  public string? Category { get; init; }
}
