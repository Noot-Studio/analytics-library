using Noot.Analytics;

[TestClass]
public class MovementSamplerTests
{
	[TestMethod]
	public void FirstSample_AlwaysEmits()
	{
		var sampler = new MovementSampler( 1f, 64f );

		Assert.IsTrue( sampler.ShouldSample( new Vector3( 0f, 0f, 0f ), 0.0 ) );
	}

	[TestMethod]
	public void WithinInterval_NeverEmits_EvenWhenMoved()
	{
		var sampler = new MovementSampler( 1f, 64f );
		sampler.ShouldSample( Vector3.Zero, 0.0 );

		Assert.IsFalse( sampler.ShouldSample( new Vector3( 1000f, 0f, 0f ), 0.5 ) );
	}

	[TestMethod]
	public void AfterInterval_EmitsWhenMovedEnough()
	{
		var sampler = new MovementSampler( 1f, 64f );
		sampler.ShouldSample( Vector3.Zero, 0.0 );

		Assert.IsTrue( sampler.ShouldSample( new Vector3( 100f, 0f, 0f ), 1.5 ) );
	}

	[TestMethod]
	public void Stationary_EmitsNothing_AndKeepsThrottling()
	{
		var sampler = new MovementSampler( 1f, 64f );
		sampler.ShouldSample( Vector3.Zero, 0.0 );

		// Barely moved: rejected, and the rejection still resets the interval
		// window so the next check inside it is throttled too.
		Assert.IsFalse( sampler.ShouldSample( new Vector3( 10f, 0f, 0f ), 1.5 ) );
		Assert.IsFalse( sampler.ShouldSample( new Vector3( 1000f, 0f, 0f ), 2.0 ) );
		// Past the rescheduled window and far from the LAST RECORDED position.
		Assert.IsTrue( sampler.ShouldSample( new Vector3( 1000f, 0f, 0f ), 2.6 ) );
	}

	[TestMethod]
	public void DistanceComparesAgainstLastRecordedSample_NotLastCheck()
	{
		var sampler = new MovementSampler( 1f, 64f );
		sampler.ShouldSample( Vector3.Zero, 0.0 );

		// Creeping 40 units per interval: each check is < 64 from the recorded
		// sample at origin... until cumulative drift crosses the threshold.
		Assert.IsFalse( sampler.ShouldSample( new Vector3( 40f, 0f, 0f ), 1.5 ) );
		Assert.IsTrue( sampler.ShouldSample( new Vector3( 80f, 0f, 0f ), 3.0 ) );
	}

	[TestMethod]
	public void TinyInterval_IsFlooredToQuarterSecond()
	{
		var sampler = new MovementSampler( 0f, 0f );
		sampler.ShouldSample( Vector3.Zero, 0.0 );

		// 0.1s later: still inside the floored 0.25s window.
		Assert.IsFalse( sampler.ShouldSample( new Vector3( 500f, 0f, 0f ), 0.1 ) );
		Assert.IsTrue( sampler.ShouldSample( new Vector3( 500f, 0f, 0f ), 0.3 ) );
	}

	[TestMethod]
	public void Reset_AllowsImmediateResample()
	{
		var sampler = new MovementSampler( 10f, 64f );
		sampler.ShouldSample( Vector3.Zero, 0.0 );

		sampler.Reset();

		Assert.IsTrue( sampler.ShouldSample( Vector3.Zero, 0.1 ) );
	}
}
