using System.Collections.Generic;
using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Optional drop-in that reports the local client's frame rate. Each render frame
/// feeds a <see cref="FpsSampler"/>; once the window closes it emits ONE
/// <c>fps_sample</c> event carrying the window's average FPS (and the active map,
/// so the performance dashboard's per-map table can attribute it).
///
/// Unlike the spatial trackers this is intentionally NOT host-only: every client
/// runs its own Analytics session, so each measures and reports its own local FPS
/// — that is the whole point of player frame-rate telemetry.
/// </summary>
[Title( "Analytics FPS Tracker" )]
[Category( "Analytics" )]
[Icon( "speed" )]
public sealed class AnalyticsFpsComponent : Component
{
	/// <summary>Seconds of frames averaged into one sample. Each window is one event. Floored at 1s.</summary>
	[Property, Range( 1f, 60f )] public float SampleWindowSeconds { get; set; } = 10f;

	FpsSampler? _sampler;

	protected override void OnEnabled() => _sampler = new FpsSampler( SampleWindowSeconds );

	protected override void OnUpdate()
	{
		if ( !Analytics.IsInitialized || _sampler is null )
			return;

		if ( _sampler.Tick( RealTime.Delta ) is not { } fps )
			return;

		var properties = new Dictionary<string, object> { ["fps"] = fps };

		// Attribute the sample to the current map so "Performance by map" can group
		// it. Empty (no loaded map) is omitted — those samples still feed the daily
		// percentiles, they just don't land in the per-map table.
		var map = CurrentMap();
		if ( !string.IsNullOrEmpty( map ) )
			properties["map"] = map;

		Analytics.Track( "fps_sample", properties: properties );
	}

	string CurrentMap()
	{
		if ( !Scene.IsValid() )
			return "";

		foreach ( var map in Scene.GetAllComponents<MapInstance>() )
			return map.MapName ?? "";

		return "";
	}
}
