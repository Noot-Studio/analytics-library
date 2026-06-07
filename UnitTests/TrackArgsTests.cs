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

	[TestMethod]
	public void CaptureArgs_NullDeclaringType_FallsBackToPositional()
	{
		// Static methods have no instance, so the declaring type is null — args
		// must still be captured under positional names rather than dropped.
		var props = TrackAttribute.CaptureArgs( 1, null, new object[] { 42, "x" } );

		Assert.AreEqual( 42, props["arg0"] );
		Assert.AreEqual( "x", props["arg1"] );
	}

	[TestMethod]
	public void CaptureArgs_AllNullArgs_ReturnsNull()
	{
		Assert.IsNull( TrackAttribute.CaptureArgs( -1, typeof( TrackArgsTests ), new object[] { null, null } ) );
	}
}
