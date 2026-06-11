using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Attach to an entity (typically a player pawn) to record its position as
/// periodic spatial events, feeding movement/traversal heatmaps.
///
/// Traffic is bounded by design: samples are throttled to one per
/// <see cref="SampleIntervalSeconds"/> (floored at 0.25s), stationary entities
/// emit nothing (<see cref="MinMoveDistance"/>), only the host samples by
/// default (one emitter per entity instead of one per client), and events ride
/// the SDK's existing batched flush — never one HTTP request per sample.
/// </summary>
[Title( "Analytics Movement Tracker" )]
[Category( "Analytics" )]
[Icon( "route" )]
public sealed class AnalyticsMovementComponent : Component
{
	/// <summary>Event type recorded for each sample.</summary>
	[Property] public string EventType { get; set; } = "position_sample";

	/// <summary>Seconds between samples. Clamped to a 0.25s minimum.</summary>
	[Property, Range( 0.25f, 30f )] public float SampleIntervalSeconds { get; set; } = 2f;

	/// <summary>
	/// Minimum distance (units) the entity must move since its last recorded
	/// sample. Stationary entities emit no events at all.
	/// </summary>
	[Property] public float MinMoveDistance { get; set; } = 64f;

	/// <summary>
	/// Sample on the host only. Keeps one emitter per entity in multiplayer —
	/// with this off, every connected client records its own duplicate stream.
	/// </summary>
	[Property] public bool HostOnly { get; set; } = true;

	MovementSampler? _sampler;

	protected override void OnEnabled()
	{
		_sampler = new MovementSampler( SampleIntervalSeconds, MinMoveDistance );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Analytics.IsInitialized || _sampler is null )
			return;

		if ( HostOnly && !Networking.IsHost )
			return;

		var position = WorldPosition;
		if ( !_sampler.ShouldSample( position, Time.Now ) )
			return;

		Analytics.Track( EventType, position: position, playerId: OwnerPlayerId() );
	}

	// Attribute the sample to the entity's network owner when it has one, so
	// per-player movement heatmaps line up with the rest of their events.
	// Unowned (world/NPC) entities fall back to the session's default id.
	string? OwnerPlayerId()
	{
		var owner = GameObject.Network.Active ? GameObject.Network.Owner : null;
		return owner is null ? null : AnonymousId.Hash( owner.SteamId );
	}
}
