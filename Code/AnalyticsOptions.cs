namespace Noot.Analytics;

/// <summary>Configuration for <see cref="Analytics.Init"/>.</summary>
public sealed class AnalyticsOptions
{
	/// <summary>Ingestion base URL. Override for self-host / local dev.</summary>
	public string IngestUrl { get; set; } = "https://ingest.sbox-analytics.com";

	/// <summary>Emit session_start on Init and session_end on Shutdown.</summary>
	public bool TrackSessions { get; set; } = true;

	/// <summary>Background flush cadence in real seconds.</summary>
	public float FlushIntervalSeconds { get; set; } = 10f;

	/// <summary>Events per send. Clamped to the ingest cap (500).</summary>
	public int MaxBatchSize { get; set; } = 50;

	/// <summary>Override the anonymous player id. Null = hash of the local SteamID.</summary>
	public string? PlayerId { get; set; }
}
