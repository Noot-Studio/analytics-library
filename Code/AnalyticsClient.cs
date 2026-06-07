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

		var ev = new AnalyticsEvent
		{
			Type = type,
			SessionId = SessionId,
			PlayerId = playerId ?? PlayerId,
			Properties = properties,
			Scene = scene ?? "",
			Position = position,
			Timestamp = DateTime.UtcNow,
		};

		if ( _buffer.Add( ev ) && _buffer.Count >= _options.MaxBatchSize )
			_ = FlushAsync();
	}

	public async Task FlushAsync()
	{
		if ( !Enabled )
			return;

		var batch = _buffer.TakeBatch( _options.MaxBatchSize );
		if ( batch.Count == 0 )
			return;

		var ok = await _sender.SendAsync( batch, _apiKey, _options.IngestUrl );
		if ( !ok )
			_buffer.Requeue( batch );
	}

	/// <summary>Emit session_end, stop the loop, and flush everything that remains.</summary>
	public async Task ShutdownAsync()
	{
		if ( Enabled && _options.TrackSessions )
			Enqueue( "session_end" );

		_cts?.Cancel();
		await FlushAsync();
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

	// --- test-only inspection helpers ---
	internal AnalyticsEvent PeekLast()
	{
		var batch = _buffer.TakeBatch();
		var last = batch[^1];
		_buffer.Requeue( batch );
		return last;
	}
}
