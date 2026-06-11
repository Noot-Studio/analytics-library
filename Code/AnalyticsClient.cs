using System;
using System.Threading;
using System.Threading.Tasks;
using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// The core SDK instance: owns the session id, anonymous player id, event buffer,
/// sender, and a self-driven flush loop. Usable with no component.
/// </summary>
public sealed class AnalyticsClient
{
	readonly string _apiKey;
	readonly AnalyticsOptions _options;
	readonly IEventSender _sender;
	readonly EventBuffer _buffer = new();
	CancellationTokenSource? _cts;

	public string SessionId { get; }
	public string PlayerId { get; }
	public bool Enabled { get; }
	public int PendingCount => _buffer.Count;

	public AnalyticsClient( string apiKey, AnalyticsOptions options, IEventSender sender )
	{
		_apiKey = apiKey;
		_options = options;
		_sender = sender;
		SessionId = Guid.NewGuid().ToString();
		PlayerId = options.PlayerId ?? AnonymousId.Hash( Connection.Local?.SteamId ?? 0UL );
		Enabled = !string.IsNullOrEmpty( apiKey );

		if ( !Enabled )
			Log.Warning( "[Analytics] no API key — SDK disabled, events will be dropped." );
	}

	/// <summary>Emit session_start (if enabled) and begin the flush loop.</summary>
	public void Start()
	{
		if ( !Enabled )
			return;

		_cts = new CancellationTokenSource();
		if ( _options.TrackSessions )
			Enqueue( "session_start" );

		_ = FlushLoop( _cts.Token );
	}

	public void Enqueue( string type, object? properties = null, string? scene = null,
		Vector3? position = null, string? playerId = null )
	{
		if ( !Enabled )
			return;

		var resolvedPlayerId = playerId ?? PlayerId;

		// Explicit args always win; the option providers only fill gaps. They run
		// user code, so a throwing provider degrades to "no scene/position" rather
		// than dropping the event.
		if ( scene is null && _options.SceneProvider is not null )
		{
			try { scene = _options.SceneProvider(); }
			catch ( Exception e ) { Log.Warning( $"[Analytics] SceneProvider threw: {e.Message}" ); }
		}

		if ( position is null && _options.PositionResolver is not null )
		{
			try { position = _options.PositionResolver( resolvedPlayerId ); }
			catch ( Exception e ) { Log.Warning( $"[Analytics] PositionResolver threw: {e.Message}" ); }
		}

		var ev = new AnalyticsEvent
		{
			Type = type,
			SessionId = SessionId,
			PlayerId = resolvedPlayerId,
			Properties = properties,
			Scene = scene ?? "",
			Position = position,
			Timestamp = DateTime.UtcNow,
		};

		if ( _buffer.Add( ev ) && _buffer.Count >= _options.MaxBatchSize )
			_ = FlushAsync();
	}

	/// <summary>
	/// Send one batch. Best-effort and non-exclusive: concurrent calls each take an
	/// independent slice of the buffer, so a failed send re-queues behind newer
	/// events (ordering is approximate). Never throws — a misbehaving sender is
	/// caught and logged so fire-and-forget callers don't leak unobserved faults.
	/// </summary>
	public async Task FlushAsync()
	{
		if ( !Enabled )
			return;

		var batch = _buffer.TakeBatch( _options.MaxBatchSize );
		if ( batch.Count == 0 )
			return;

		try
		{
			var ok = await _sender.SendAsync( batch, _apiKey, _options.IngestUrl );
			if ( !ok )
				_buffer.Requeue( batch );
		}
		catch ( Exception e )
		{
			_buffer.Requeue( batch );
			Log.Warning( $"[Analytics] flush error: {e.Message}" );
		}
	}

	/// <summary>Emit session_end, stop the loop, and flush everything that remains.</summary>
	public async Task ShutdownAsync()
	{
		if ( Enabled && _options.TrackSessions )
			Enqueue( "session_end" );

		_cts?.Cancel();
		await FlushAsync();
		_cts?.Dispose();
		_cts = null;
	}

	async Task FlushLoop( CancellationToken ct )
	{
		while ( !ct.IsCancellationRequested )
		{
			try
			{
				await GameTask.DelayRealtimeSeconds( _options.FlushIntervalSeconds, ct );
			}
			catch ( OperationCanceledException )
			{
				break;
			}

			await FlushAsync();
		}
	}

	// --- test-only inspection helper ---
	// Destructive: drains the buffer and re-queues it. Single-threaded test use only,
	// not safe to call while the flush loop is running.
	internal AnalyticsEvent PeekLast()
	{
		var batch = _buffer.TakeBatch();
		var last = batch[^1];
		_buffer.Requeue( batch );
		return last;
	}
}
