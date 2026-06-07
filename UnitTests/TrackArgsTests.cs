using Noot.Analytics;

[TestClass]
public class TrackArgsTests
{
	public void Sample( float height, string surface ) { }

	[TestMethod]
	public void CaptureArgs_UnknownIdentity_FallsBackToPositional()
	{
		var props = TrackAttribute.CaptureArgs( -1, typeof( TrackArgsTests ), new object[] { 3.2f, "grass" } );

		Assert.AreEqual( 3.2f, props["arg0"] );
		Assert.AreEqual( "grass", props["arg1"] );
	}

	[TestMethod]
	public void CaptureArgs_NoArgs_ReturnsNull()
	{
		Assert.IsNull( TrackAttribute.CaptureArgs( -1, typeof( TrackArgsTests ), new object[0] ) );
	}
}
