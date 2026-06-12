using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Noot.Analytics;

[TestClass]
public class AutoTaggingTests
{
	sealed class FakeSender : IEventSender
	{
		public List<AnalyticsEvent> Sent { get; } = new();

		public Task<bool> SendAsync( List<AnalyticsEvent> batch, string publishableKey, string ingestUrl )
		{
			Sent.AddRange( batch );
			return Task.FromResult( true );
		}
	}

	static AnalyticsOptions Opts() =>
		new() { TrackSessions = false, MaxBatchSize = 50, PlayerId = "anon", FlushIntervalSeconds = 999f };

	[TestMethod]
	public void SceneProvider_FillsScene_WhenNotExplicit()
	{
		var opts = Opts();
		opts.SceneProvider = () => "dm_arena";
		var client = new AnalyticsClient( "pk_test", opts, new FakeSender() );

		client.Enqueue( "player_death" );

		Assert.AreEqual( "dm_arena", client.PeekLast().Scene );
	}

	[TestMethod]
	public void ExplicitScene_WinsOverProvider()
	{
		var opts = Opts();
		opts.SceneProvider = () => "dm_arena";
		var client = new AnalyticsClient( "pk_test", opts, new FakeSender() );

		client.Enqueue( "player_death", scene: "custom_map" );

		Assert.AreEqual( "custom_map", client.PeekLast().Scene );
	}

	[TestMethod]
	public void PositionResolver_FillsPosition_AndReceivesResolvedPlayerId()
	{
		var opts = Opts();
		string? seenPlayerId = null;
		opts.PositionResolver = pid =>
		{
			seenPlayerId = pid;
			return new Vector3( 1f, 2f, 3f );
		};
		var client = new AnalyticsClient( "pk_test", opts, new FakeSender() );

		client.Enqueue( "player_death", playerId: "anon_other" );

		Assert.AreEqual( "anon_other", seenPlayerId );
		Assert.AreEqual( new Vector3( 1f, 2f, 3f ), client.PeekLast().Position );
	}

	[TestMethod]
	public void PositionResolver_FallsBackToClientPlayerId_WhenNoneGiven()
	{
		var opts = Opts();
		string? seenPlayerId = null;
		opts.PositionResolver = pid =>
		{
			seenPlayerId = pid;
			return null;
		};
		var client = new AnalyticsClient( "pk_test", opts, new FakeSender() );

		client.Enqueue( "custom_event" );

		Assert.AreEqual( "anon", seenPlayerId );
		Assert.IsNull( client.PeekLast().Position );
	}

	[TestMethod]
	public void ExplicitPosition_WinsOverResolver()
	{
		var opts = Opts();
		var resolverCalls = 0;
		opts.PositionResolver = _ =>
		{
			resolverCalls++;
			return Vector3.Zero;
		};
		var client = new AnalyticsClient( "pk_test", opts, new FakeSender() );

		client.Enqueue( "player_death", position: new Vector3( 9f, 9f, 9f ) );

		Assert.AreEqual( 0, resolverCalls );
		Assert.AreEqual( new Vector3( 9f, 9f, 9f ), client.PeekLast().Position );
	}

	[TestMethod]
	public void ThrowingProviders_DegradeToUntagged_EventStillEnqueued()
	{
		var opts = Opts();
		opts.SceneProvider = () => throw new InvalidOperationException( "boom" );
		opts.PositionResolver = _ => throw new InvalidOperationException( "boom" );
		var client = new AnalyticsClient( "pk_test", opts, new FakeSender() );

		client.Enqueue( "custom_event" );

		Assert.AreEqual( 1, client.PendingCount );
		var ev = client.PeekLast();
		Assert.AreEqual( "", ev.Scene );
		Assert.IsNull( ev.Position );
	}
}
