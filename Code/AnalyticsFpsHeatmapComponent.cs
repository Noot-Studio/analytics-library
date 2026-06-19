using System.Collections.Generic;
using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Attach to a player pawn to record WHERE the local client's frame rate drops.
/// Every <see cref="SampleIntervalSeconds"/> it emits ONE positioned
/// <c>fps_sample</c> event carrying the window's average FPS and the pawn's
/// world position, so the spatial heatmap can voxelize frame rate and surface
/// the avg / min / max / sum FPS per region of the map.
///
/// Unlike the dwell/density trackers this is per-client, not host-only: each
/// machine measures its OWN frame rate, so only the client that OWNS this pawn
/// samples it — otherwise a client would tag its own FPS onto every replicated
/// remote pawn. The same <c>fps_sample</c> events also feed the (non-spatial)
/// frame-rate percentiles on the Performance dashboard.
/// </summary>
[Title( "Analytics FPS Heatmap Tracker" )]
[Category( "Analytics" )]
[Icon( "thermostat" )]
public sealed class AnalyticsFpsHeatmapComponent : Component
{
	/// <summary>Seconds of frames averaged into one positioned sample. Each window is one event. Floored at 1s.</summary>
	[Property, Range( 1f, 30f )] public float SampleIntervalSeconds { get; set; } = 2f;

	FpsSampler? _sampler;

	protected override void OnEnabled() => _sampler = new FpsSampler( SampleIntervalSeconds );

	protected override void OnUpdate()
	{
		if ( !Analytics.IsInitialized || _sampler is null )
			return;

		// Frame rate is local to each machine. Only the owning client samples a
		// networked pawn; non-networked (singleplayer) pawns are always local.
		if ( GameObject.Network.Active && !GameObject.Network.IsOwner )
			return;

		if ( _sampler.Tick( RealTime.Delta ) is not { } fps )
			return;

		Analytics.Track( "fps_sample",
			properties: new Dictionary<string, object> { ["fps"] = fps },
			position: WorldPosition,
			playerId: OwnerPlayerId() );
	}

	// Attribute the sample to the pawn's network owner so per-player FPS lines up
	// with their other events. Unowned (world/local) pawns fall back to the
	// session default id.
	string? OwnerPlayerId()
	{
		var owner = GameObject.Network.Active ? GameObject.Network.Owner : null;
		return owner is null ? null : AnonymousId.Hash( owner.SteamId );
	}
}
