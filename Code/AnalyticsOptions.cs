using System;
using Sandbox;

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

	/// <summary>
	/// Default scene for events tracked without an explicit one. Called per event;
	/// return null/empty for "no scene". AnalyticsComponent wires this to the
	/// active scene's title automatically.
	/// </summary>
	public Func<string?>? SceneProvider { get; set; }

	/// <summary>
	/// Default position for events tracked without an explicit one. Receives the
	/// event's resolved anonymous player id; return null to leave the event
	/// non-spatial (preferred over guessing — a wrong position renders as a
	/// confident, misleading heatmap hotspot, a missing one just drops out).
	/// </summary>
	public Func<string, Vector3?>? PositionResolver { get; set; }
}
