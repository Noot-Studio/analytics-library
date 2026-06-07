using System;
using System.Collections.Generic;
using System.Text;
using Sandbox;

namespace Noot.Analytics;

/// <summary>Thread-safe FIFO of pending events. Guards property size, caps capacity.</summary>
public sealed class EventBuffer
{
	/// <summary>Hard ingest cap: never more than this many events per request.</summary>
	public const int MaxBatchSize = 500;

	/// <summary>Per-event properties byte cap enforced by the ingest API.</summary>
	public const int MaxPropertiesBytes = 16 * 1024;

	readonly int _capacity;
	readonly Queue<AnalyticsEvent> _queue = new();
	readonly object _lock = new();

	public EventBuffer( int capacity = 10_000 ) => _capacity = Math.Max( 1, capacity );

	public int Count
	{
		get
		{
			lock ( _lock )
				return _queue.Count;
		}
	}

	/// <summary>Enqueue. Returns false (and logs) if properties exceed the 16 KB cap.</summary>
	public bool Add( AnalyticsEvent ev )
	{
		if ( ev.Properties is not null )
		{
			var bytes = Encoding.UTF8.GetByteCount( Json.Serialize( ev.Properties ) );
			if ( bytes > MaxPropertiesBytes )
			{
				Log.Warning( $"[Analytics] dropped '{ev.Type}': properties {bytes} bytes > {MaxPropertiesBytes}" );
				return false;
			}
		}

		lock ( _lock )
		{
			_queue.Enqueue( ev );
			while ( _queue.Count > _capacity )
				_queue.Dequeue();
		}

		return true;
	}

	/// <summary>Pop up to <paramref name="max"/> events (clamped to <see cref="MaxBatchSize"/>).</summary>
	public List<AnalyticsEvent> TakeBatch( int max = MaxBatchSize )
	{
		max = Math.Min( max, MaxBatchSize );
		lock ( _lock )
		{
			var n = Math.Min( max, _queue.Count );
			var list = new List<AnalyticsEvent>( n );
			for ( var i = 0; i < n; i++ )
				list.Add( _queue.Dequeue() );
			return list;
		}
	}

	/// <summary>Return a failed batch to the buffer (best-effort ordering).</summary>
	public void Requeue( IEnumerable<AnalyticsEvent> events )
	{
		// Materialize before locking so a lazy/deferred enumerable can't run caller
		// code (or re-enter this lock) while the buffer is held.
		var items = events as ICollection<AnalyticsEvent> ?? new List<AnalyticsEvent>( events );
		lock ( _lock )
		{
			foreach ( var ev in items )
			{
				_queue.Enqueue( ev );
				while ( _queue.Count > _capacity )
					_queue.Dequeue();
			}
		}
	}
}
