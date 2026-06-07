using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Optional drop-in. Auto-configures the Analytics core from editor fields and
/// emits the default events that need a scene/network presence
/// (scene_loaded, player_connected, player_disconnected). Session start/end are
/// owned by the core. Connect/disconnect fire on the host only.
/// </summary>
[Title( "Analytics" )]
[Category( "Analytics" )]
[Icon( "analytics" )]
public sealed class AnalyticsComponent : Component, Component.INetworkListener
{
	[Property] public string ApiKey { get; set; } = "";
	[Property] public string IngestUrl { get; set; } = "https://ingest.sbox-analytics.com";
	[Property] public bool TrackSessions { get; set; } = true;
	[Property] public bool TrackSceneLoads { get; set; } = true;
	[Property] public bool TrackConnections { get; set; } = true;
	[Property] public float FlushIntervalSeconds { get; set; } = 10f;
	[Property] public int MaxBatchSize { get; set; } = 50;

	bool _ownsClient;

	protected override void OnEnabled()
	{
		if ( Analytics.IsInitialized )
			return; // core already configured from code — just attach.

		Analytics.Init( ApiKey, new AnalyticsOptions
		{
			IngestUrl = IngestUrl,
			TrackSessions = TrackSessions,
			FlushIntervalSeconds = FlushIntervalSeconds,
			MaxBatchSize = MaxBatchSize,
		} );
		_ownsClient = true;
	}

	protected override void OnStart()
	{
		if ( TrackSceneLoads )
		{
			var sceneTitle = "";
			foreach ( var info in Scene.GetAllComponents<SceneInformation>() )
			{
				sceneTitle = info.Title;
				break;
			}
			Analytics.Track( "scene_loaded", scene: sceneTitle );
		}
	}

	protected override void OnDestroy()
	{
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
