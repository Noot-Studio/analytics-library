using System;
using System.Collections.Generic;
using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Attach to an entity (typically a player pawn) to record its ordered path
/// through the world. Positions are sampled on an interval with a move-distance
/// gate; a window flush simplifies the buffered path (Ramer–Douglas–Peucker) and
/// emits ONE event carrying the surviving points in chronological order.
///
/// HIGHEST event cost of the spatial trackers: per-player paths cannot be
/// aggregated server-side the way dwell/heatmap cells can, so every player's
/// trajectory is stored in full. Prefer <see cref="AnalyticsDwellComponent"/> or
/// <see cref="AnalyticsHeatmapComponent"/> unless you need exact paths. See
/// <see cref="EstimatedEvents"/> before enabling.
/// </summary>
[Title( "Analytics Trajectory Tracker" )]
[Category( "Analytics" )]
[Icon( "timeline" )]
public sealed class AnalyticsTrajectoryComponent : Component
{
	/// <summary>Seconds between position samples. Clamped to a 0.1s minimum.</summary>
	[Property, Range( 0.1f, 10f )] public float SampleIntervalSeconds { get; set; } = 0.5f;

	/// <summary>Minimum distance (units) moved since the last sample. Stationary entities record nothing.</summary>
	[Property] public float MinMoveDistance { get; set; } = 32f;

	/// <summary>Seconds between flushes. Each flush is one event. Floored at 1s.</summary>
	[Property, Range( 1f, 120f )] public float FlushIntervalSeconds { get; set; } = 10f;

	/// <summary>Simplification tolerance (units). Points within this of the line are dropped.</summary>
	[Property] public float Epsilon { get; set; } = 32f;

	/// <summary>
	/// Sample on the host only. Keeps one emitter per entity in multiplayer —
	/// with this off, every connected client records its own duplicate stream.
	/// </summary>
	[Property] public bool HostOnly { get; set; } = true;

	/// <summary>Read-only estimate of the event volume this configuration implies.</summary>
	[Property, Title( "Estimated Events" )]
	public string EstimatedEvents
	{
		get
		{
			var interval = FlushWindow.FloorInterval( FlushIntervalSeconds );
			return $"~{60f / interval:0.#} events/min/player at {interval:0.#}s flush (per-player paths, not server-aggregable)";
		}
	}

	MovementSampler? _sampler;
	readonly List<Vector3> _points = new();
	readonly List<long> _times = new();
	FlushWindow? _window;

	static bool _advised;

	protected override void OnEnabled()
	{
		_sampler = new MovementSampler( SampleIntervalSeconds, MinMoveDistance );
		_points.Clear();
		_times.Clear();
		_window = new FlushWindow( FlushIntervalSeconds );
		_window.Schedule( Time.Now );
		WarnOnce();
	}

	// One-time developer advisory. Fired the first time ANY instance of this
	// high-volume tracker is enabled — not once per spawned entity, which floods
	// the console when a prefab carrying the tracker is cloned many times.
	static void WarnOnce()
	{
		if ( _advised )
			return;
		_advised = true;
		Log.Warning( "[Analytics] AnalyticsTrajectoryComponent enabled — per-player trajectories cannot be aggregated server-side and are the highest event cost; prefer Dwell/Heatmap unless you need exact paths." );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Analytics.IsInitialized || _sampler is null )
			return;

		if ( HostOnly && !Networking.IsHost )
			return;

		var position = WorldPosition;
		if ( _sampler.ShouldSample( position, Time.Now ) )
		{
			_points.Add( position );
			_times.Add( DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() );
		}

		if ( _window!.IsDue( Time.Now ) )
			Flush();
	}

	protected override void OnDisabled() => Flush();

	void Flush()
	{
		if ( _window is null )
			return;
		_window.Schedule( Time.Now );

		// Two or fewer points isn't a path worth shipping; keep them buffered so the
		// next window can extend them rather than emitting a degenerate trajectory.
		if ( _points.Count <= 2 )
			return;

		var simplified = LineSimplifier.Simplify( _points, Epsilon );

		// Map each surviving point back to its sample time. Simplify preserves
		// order and only drops points, so a forward scan recovers the timestamps.
		var points = new List<object[]>( simplified.Count );
		var cursor = 0;
		foreach ( var p in simplified )
		{
			while ( cursor < _points.Count && _points[cursor] != p )
				cursor++;

			var tMs = cursor < _times.Count ? _times[cursor] : _times[^1];
			points.Add( new object[] { p.x, p.y, p.z, tMs } );
			cursor++;
		}

		Analytics.Track( "trajectory",
			properties: new { points },
			playerId: OwnerPlayerId() );

		// Carry the last point over as the next window's anchor so the path stays
		// continuous across flushes.
		var lastPoint = _points[^1];
		var lastTime = _times[^1];
		_points.Clear();
		_times.Clear();
		_points.Add( lastPoint );
		_times.Add( lastTime );
	}

	// Attribute samples to the entity's network owner so per-player trajectories
	// line up with their other events. Unowned (world/NPC) entities fall back to
	// the session default id.
	string? OwnerPlayerId()
	{
		var owner = GameObject.Network.Active ? GameObject.Network.Owner : null;
		return owner is null ? null : AnonymousId.Hash( owner.SteamId );
	}
}
