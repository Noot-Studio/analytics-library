using Noot.Analytics;

[TestClass]
public class FpsSamplerTests
{
	[TestMethod]
	public void Tick_ReturnsNull_WhileWindowOpen()
	{
		var sampler = new FpsSampler( 10f );

		Assert.IsNull( sampler.Tick( 0.016 ) );
	}

	[TestMethod]
	public void Tick_ReturnsAverageFps_WhenWindowCloses()
	{
		var sampler = new FpsSampler( 1f );

		// Four 0.25s frames span exactly the 1s window: 4 frames / 1s = 4 FPS.
		Assert.IsNull( sampler.Tick( 0.25 ) );
		Assert.IsNull( sampler.Tick( 0.25 ) );
		Assert.IsNull( sampler.Tick( 0.25 ) );
		Assert.AreEqual( 4f, sampler.Tick( 0.25 ) );
	}

	[TestMethod]
	public void Tick_RearmsAfterEmitting()
	{
		var sampler = new FpsSampler( 1f );
		sampler.Tick( 0.5 );
		sampler.Tick( 0.5 ); // closes and resets the window

		// New window: a single short frame leaves it open again.
		Assert.IsNull( sampler.Tick( 0.25 ) );
	}

	[TestMethod]
	public void Tick_IgnoresNonPositiveDelta()
	{
		var sampler = new FpsSampler( 1f );

		Assert.IsNull( sampler.Tick( 0 ) );
		Assert.IsNull( sampler.Tick( -1 ) );
	}

	[TestMethod]
	public void Window_IsFlooredToOneSecond()
	{
		Assert.AreEqual( 1f, new FpsSampler( 0f ).WindowSeconds );
		Assert.AreEqual( 1f, new FpsSampler( 0.5f ).WindowSeconds );
		Assert.AreEqual( 15f, new FpsSampler( 15f ).WindowSeconds );
	}
}
