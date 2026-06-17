using System.Collections.Generic;
using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Attach to an entity (typically a player pawn) to record how long it dwells in
/// each region of the map. Every fixed tick the entity's cell gains the tick's
/// elapsed milliseconds; a window flush emits ONE event carrying every touched
/// cell — never one event per tick.
///
/// Traffic is bounded by accumulation: a player standing still adds to a single
/// cell all window, and the flush ships that cell once. See
/// <see cref="EstimatedEvents"/> for the implied event rate before enabling.
/// </summary>
[Title( "Analytics Dwell Tracker" )]
[Category( "Analytics" )]
[Icon( "hourglass_bottom" )]
public sealed class AnalyticsDwellComponent : Component
{
	/// <summary>Edge length (units) of one dwell cell. Bigger = coarser, fewer cells.</summary>
	[Property] public float CellSize { get; set; } = 128f;

	/// <summary>Seconds between flushes. Each flush is one event. Floored at 1s.</summary>
	[Property, Range( 1f, 120f )] public float FlushIntervalSeconds { get; set; } = 15f;

	/// <summary>Force a flush once this many distinct cells are dirty, bounding event size.</summary>
	[Property] public int MaxCellsPerFlush { get; set; } = 256;

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
			var interval = FlushIntervalSeconds < 1f ? 1f : FlushIntervalSeconds;
			return $"~{60f / interval:0.#} events/min/player at {interval:0.#}s flush (plus early flushes at {MaxCellsPerFlush} cells)";
		}
	}

	SpatialGrid? _grid;
	readonly CellAccumulator _accumulator = new();
	double _nextFlushTime;

	protected override void OnEnabled()
	{
		_grid = new SpatialGrid( CellSize );
		_nextFlushTime = Time.Now + FlushInterval();
		Log.Warning( "[Analytics] AnalyticsDwellComponent enabled — spatial dwell tracking is high-volume; tune CellSize/FlushIntervalSeconds and prefer host-only sampling." );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Analytics.IsInitialized || _grid is null )
			return;

		if ( HostOnly && !Networking.IsHost )
			return;

		var cell = _grid.Cell( WorldPosition );
		_accumulator.AddDwell( cell, (int)(Time.Delta * 1000f) );

		if ( Time.Now >= _nextFlushTime || _accumulator.Count >= MaxCellsPerFlush )
			Flush();
	}

	protected override void OnDisabled() => Flush();

	float FlushInterval() => FlushIntervalSeconds < 1f ? 1f : FlushIntervalSeconds;

	void Flush()
	{
		_nextFlushTime = Time.Now + FlushInterval();

		if ( _accumulator.Count == 0 )
			return;

		var drained = _accumulator.Drain();
		var cells = new List<int[]>( drained.Count );
		foreach ( var (cell, acc) in drained )
			cells.Add( new[] { cell.Item1, cell.Item2, cell.Item3, acc.DwellMs } );

		Analytics.Track( "spatial_cells",
			properties: new { kind = "dwell", cell_size = CellSize, cells },
			playerId: OwnerPlayerId() );
	}

	// Attribute samples to the entity's network owner so per-player dwell lines up
	// with their other events. Unowned (world/NPC) entities fall back to the
	// session default id.
	string? OwnerPlayerId()
	{
		var owner = GameObject.Network.Active ? GameObject.Network.Owner : null;
		return owner is null ? null : AnonymousId.Hash( owner.SteamId );
	}
}
