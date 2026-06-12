using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Optional drop-in for session and network lifecycle events. Auto-configures
/// the Analytics core from editor fields and emits the default events that
/// need a scene/network presence (scene_loaded, player_connected,
/// player_disconnected). Session start/end are owned by the core.
/// Connect/disconnect fire on the host only. For per-entity position
/// tracking, see <see cref="AnalyticsMovementComponent"/>.
/// </summary>
[Title( "Analytics Session Helper" )]
[Category( "Analytics" )]
[Icon( "analytics" )]
public sealed class AnalyticsComponent : Component, Component.INetworkListener
{
	[Property, Title( "Publishable Key" )] public string PublishableKey { get; set; } = "";
	[Property] public string IngestUrl { get; set; } = "https://ingest.sbox-analytics.com";
	[Property] public bool TrackSessions { get; set; } = true;
	[Property] public bool TrackSceneLoads { get; set; } = true;
	[Property] public bool TrackConnections { get; set; } = true;

	/// <summary>Tag every event with the active scene's title when no scene is passed explicitly.</summary>
	[Property] public bool AutoScene { get; set; } = true;

	/// <summary>
	/// Tag events with the tracked player's pawn position when no position is
	/// passed explicitly. The pawn is the player's root networked object; events
	/// whose player has no pawn stay non-spatial (no guessing).
	/// </summary>
	[Property] public bool AutoPosition { get; set; } = true;
	[Property] public float FlushIntervalSeconds { get; set; } = 10f;
	[Property] public int MaxBatchSize { get; set; } = 50;

	bool _ownsClient;
	MapInstance? _mapInstance;

	protected override void OnEnabled()
	{
		if ( Analytics.IsInitialized )
			return; // core already configured from code — just attach.

		Analytics.Init( PublishableKey, new AnalyticsOptions
		{
			IngestUrl = IngestUrl,
			TrackSessions = TrackSessions,
			FlushIntervalSeconds = FlushIntervalSeconds,
			MaxBatchSize = MaxBatchSize,
			SceneProvider = AutoScene ? GetSceneTitle : null,
			PositionResolver = AutoPosition ? ResolvePlayerPosition : null,
		} );
		_ownsClient = true;
	}

	protected override void OnStart()
	{
		if ( !TrackSceneLoads )
			return;

		Analytics.Track( "scene_loaded", scene: GetSceneTitle() );

		// Maps load async after scene start; emit map_loaded once the instance
		// finishes so spatial data recorded against the map has a marker event.
		foreach ( var map in Scene.GetAllComponents<MapInstance>() )
		{
			_mapInstance = map;
			_mapInstance.OnMapLoaded += OnMapLoaded;
			break;
		}
	}

	void OnMapLoaded() =>
		Analytics.Track( "map_loaded", properties: new { map = _mapInstance?.MapName ?? "" }, scene: GetSceneTitle() );

	// The scene tag is the heatmap's only spatial grouping key, and a loaded
	// MapInstance — not the scene — defines the coordinate space. Append the
	// map ident so the same scene loading different maps never mixes voxels.
	string GetSceneTitle()
	{
		if ( !Scene.IsValid() )
			return "";

		var title = Scene.Name ?? "";
		foreach ( var info in Scene.GetAllComponents<SceneInformation>() )
		{
			title = info.Title;
			break;
		}

		foreach ( var map in Scene.GetAllComponents<MapInstance>() )
		{
			if ( !string.IsNullOrEmpty( map.MapName ) )
				return $"{title}:{map.MapName}";
			break;
		}

		return title;
	}

	// Pawn lookup: the root networked object owned by the connection whose
	// hashed SteamID matches the event's anonymous player id. Local (unhashed)
	// ids fall back to Connection.Local. Null when no pawn exists — the event
	// stays non-spatial rather than getting a wrong position.
	Vector3? ResolvePlayerPosition( string playerId )
	{
		if ( !Scene.IsValid() || string.IsNullOrEmpty( playerId ) )
			return null;

		foreach ( var go in Scene.GetAllObjects( true ) )
		{
			if ( !go.Network.Active || go.Network.RootGameObject != go )
				continue;

			var owner = go.Network.Owner;
			if ( owner is null )
				continue;

			if ( AnonymousId.Hash( owner.SteamId ) == playerId )
				return go.WorldPosition;
		}

		return null;
	}

	protected override void OnDestroy()
	{
		if ( _mapInstance.IsValid() )
			_mapInstance.OnMapLoaded -= OnMapLoaded;

		if ( _ownsClient )
			Analytics.Shutdown(); // emits session_end + final flush
	}

	void INetworkListener.OnActive( Connection channel )
	{
		if ( TrackConnections )
			Analytics.Track( "player_connected", playerId: AnonymousId.Hash( channel.SteamId ) );
	}

	void INetworkListener.OnDisconnected( Connection channel )
	{
		if ( TrackConnections )
			Analytics.Track( "player_disconnected", playerId: AnonymousId.Hash( channel.SteamId ) );
	}
}
