using System.Threading.Tasks;
using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Entry point for the s&amp;box Analytics SDK. Initialize once, then track events
/// from anywhere. Works with no component; the optional AnalyticsComponent and
/// [Track] attribute both funnel into <see cref="Track"/>.
/// </summary>
public static class Analytics
{
	static AnalyticsClient? _client;
	static bool _warned;

	public static bool IsInitialized => _client is not null;

	/// <summary>Configure and start the SDK. Idempotent: a second call is ignored.</summary>
	public static void Init( string apiKey, AnalyticsOptions? options = null )
	{
		if ( _client is not null )
		{
			Log.Warning( "[Analytics] Init called twice; ignoring." );
			return;
		}

		_client = new AnalyticsClient( apiKey, options ?? new AnalyticsOptions(), new HttpEventSender() );
		_client.Start();
	}

	/// <summary>Record a custom event. No-op (with a one-time warning) before Init.</summary>
	public static void Track( string type, object? properties = null, string? scene = null,
		Vector3? position = null, string? playerId = null )
	{
		if ( _client is null )
		{
			WarnNotInitialized();
			return;
		}

		_client.Enqueue( type, properties, scene, position, playerId );
	}

	/// <summary>Send buffered events immediately.</summary>
	public static void Flush() => _ = _client?.FlushAsync();

	/// <summary>Emit session_end, flush, and tear down.</summary>
	public static void Shutdown()
	{
		var client = _client;
		_client = null;
		_ = client?.ShutdownAsync();
	}

	static void WarnNotInitialized()
	{
		if ( _warned )
			return;
		_warned = true;
		Log.Warning( "[Analytics] Track called before Init — event dropped. Call Analytics.Init or add an AnalyticsComponent." );
	}

	// --- test seams ---
	internal static void InitForTests( IEventSender sender, AnalyticsOptions options )
	{
		_client = new AnalyticsClient( "pk_test", options, sender );
		_client.Start();
	}

	internal static Task FlushAsyncForTests() => _client?.FlushAsync() ?? Task.CompletedTask;

	internal static void ResetForTests()
	{
		_client = null;
		_warned = false;
	}
}
