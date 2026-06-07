using System.Collections.Generic;
using System.Threading.Tasks;
using Noot.Analytics;

[TestClass]
public class AnalyticsFacadeTests
{
	sealed class FakeSender : IEventSender
	{
		public List<AnalyticsEvent> Sent { get; } = new();
		public Task<bool> SendAsync( List<AnalyticsEvent> batch, string apiKey, string ingestUrl )
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
	public void Init_Twice_SecondIsNoOp()
	{
		Analytics.InitForTests( new FakeSender(), new AnalyticsOptions { PlayerId = "a" } );
		Analytics.InitForTests( new FakeSender(), new AnalyticsOptions { PlayerId = "b" } );
		Assert.IsTrue( Analytics.IsInitialized ); // no crash, still one client
	}
}
