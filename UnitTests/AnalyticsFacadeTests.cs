using System.Collections.Generic;
using System.Threading.Tasks;
using Noot.Analytics;

[TestClass]
public class AnalyticsFacadeTests
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

	[TestCleanup]
	public void Cleanup() => Analytics.ResetForTests();

	[TestMethod]
	public void Track_BeforeInit_NoThrow()
	{
		Analytics.ResetForTests();
		Analytics.Track( "noop" ); // must not throw
		Assert.IsFalse( Analytics.IsInitialized );
	}

	[TestMethod]
	public async Task InitThenTrackThenFlush_Sends()
	{
		var fake = new FakeSender();
		Analytics.InitForTests( fake, new AnalyticsOptions { PlayerId = "anon", FlushIntervalSeconds = 999f } );
		Analytics.Track( "custom_event", new { wave = 7 } );

		await Analytics.FlushAsyncForTests();

		Assert.IsTrue( fake.Sent.Exists( e => e.Type == "custom_event" ) );
	}

	[TestMethod]
	public async Task Init_Twice_SecondIsNoOp()
	{
		var first = new FakeSender();
		Analytics.InitForTests( first, new AnalyticsOptions { PlayerId = "a", FlushIntervalSeconds = 999f } );

		// The real Init must hit its `_client is not null` guard and do nothing —
		// it returns before constructing an HttpEventSender, so no network.
		Analytics.Init( "pk_second", new AnalyticsOptions { PlayerId = "b" } );

		Analytics.Track( "after_second_init" );
		await Analytics.FlushAsyncForTests();

		// Event landed in the FIRST client's sender → the second Init was ignored.
		Assert.IsTrue( Analytics.IsInitialized );
		Assert.IsTrue( first.Sent.Exists( e => e.Type == "after_second_init" ) );
	}
}
