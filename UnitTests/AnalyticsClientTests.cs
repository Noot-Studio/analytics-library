using System.Collections.Generic;
using System.Threading.Tasks;
using Noot.Analytics;

[TestClass]
public class AnalyticsClientTests
{
	sealed class FakeSender : IEventSender
	{
		public List<AnalyticsEvent> Sent { get; } = new();
		public bool NextResult { get; set; } = true;

		public Task<bool> SendAsync( List<AnalyticsEvent> batch, string apiKey, string ingestUrl )
		{
			if ( NextResult )
				Sent.AddRange( batch );
			return Task.FromResult( NextResult );
		}
	}

	static AnalyticsOptions Opts() =>
		new() { TrackSessions = true, MaxBatchSize = 50, PlayerId = "anon", FlushIntervalSeconds = 999f };

	[TestMethod]
	public void Constructor_EmitsSessionStart_WhenTrackSessions()
	{
		var fake = new FakeSender();
		var client = new AnalyticsClient( "pk_test", Opts(), fake );
		client.Start();

		Assert.AreEqual( 1, client.PendingCount );
	}

	[TestMethod]
	public async Task Flush_SendsBufferedEvents()
	{
		var fake = new FakeSender();
		var client = new AnalyticsClient( "pk_test", Opts(), fake );
		client.Start();
		client.Enqueue( "custom_event" );

		await client.FlushAsync();

		Assert.AreEqual( 0, client.PendingCount );
		Assert.IsTrue( fake.Sent.Exists( e => e.Type == "session_start" ) );
		Assert.IsTrue( fake.Sent.Exists( e => e.Type == "custom_event" ) );
	}

	[TestMethod]
	public async Task Flush_OnFailure_Requeues()
	{
		var fake = new FakeSender { NextResult = false };
		var client = new AnalyticsClient( "pk_test", Opts(), fake );
		client.Start();
		var before = client.PendingCount;

		await client.FlushAsync();

		Assert.AreEqual( before, client.PendingCount ); // nothing lost
	}

	[TestMethod]
	public async Task Shutdown_EmitsSessionEndAndFlushes()
	{
		var fake = new FakeSender();
		var client = new AnalyticsClient( "pk_test", Opts(), fake );
		client.Start();

		await client.ShutdownAsync();

		Assert.IsTrue( fake.Sent.Exists( e => e.Type == "session_end" ) );
	}

	[TestMethod]
	public void Disabled_WhenApiKeyEmpty()
	{
		var client = new AnalyticsClient( "", Opts(), new FakeSender() );
		client.Start();
		client.Enqueue( "x" );

		Assert.AreEqual( 0, client.PendingCount );
	}

	[TestMethod]
	public void Enqueue_UsesExplicitPlayerId_WhenGiven()
	{
		var client = new AnalyticsClient( "pk_test", Opts(), new FakeSender() );
		client.Start();
		client.Enqueue( "k", playerId: "other" );

		Assert.AreEqual( "other", client.PeekLast().PlayerId );
	}
}
