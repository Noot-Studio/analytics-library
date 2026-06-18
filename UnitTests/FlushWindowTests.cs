using Noot.Analytics;

[TestClass]
public class FlushWindowTests
{
	[TestMethod]
	public void IsDue_FalseBeforeInterval_TrueAfter()
	{
		var window = new FlushWindow( 10f );
		window.Schedule( 100.0 );

		Assert.IsFalse( window.IsDue( 105.0 ) );
		Assert.IsTrue( window.IsDue( 110.0 ) );
		Assert.IsTrue( window.IsDue( 130.0 ) );
	}

	[TestMethod]
	public void Schedule_RearmsFromGivenTime()
	{
		var window = new FlushWindow( 5f );
		window.Schedule( 0.0 );
		Assert.IsTrue( window.IsDue( 5.0 ) );

		// Re-arming after a flush pushes the next deadline forward from the new now.
		window.Schedule( 5.0 );
		Assert.IsFalse( window.IsDue( 9.0 ) );
		Assert.IsTrue( window.IsDue( 10.0 ) );
	}

	[TestMethod]
	public void FloorInterval_ClampsBelowMinimum()
	{
		Assert.AreEqual( 1f, FlushWindow.FloorInterval( 0f ) );
		Assert.AreEqual( 1f, FlushWindow.FloorInterval( 0.5f ) );
		Assert.AreEqual( 15f, FlushWindow.FloorInterval( 15f ) );
	}

	[TestMethod]
	public void Constructor_AppliesIntervalFloor()
	{
		var window = new FlushWindow( 0.2f );
		Assert.AreEqual( 1f, window.IntervalSeconds );

		// Floored to 1s, so not due at 0.5s but due at 1s.
		window.Schedule( 0.0 );
		Assert.IsFalse( window.IsDue( 0.5 ) );
		Assert.IsTrue( window.IsDue( 1.0 ) );
	}
}
