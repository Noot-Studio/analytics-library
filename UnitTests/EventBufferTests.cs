using System;
using System.Collections.Generic;
using Noot.Analytics;

[TestClass]
public class EventBufferTests
{
	static AnalyticsEvent Ev( string type = "t", object props = null ) =>
		new() { Type = type, SessionId = "s", Timestamp = DateTime.UtcNow, Properties = props };

	[TestMethod]
	public void AddThenTake_RoundTrips()
	{
		var b = new EventBuffer();
		Assert.IsTrue( b.Add( Ev() ) );
		Assert.AreEqual( 1, b.Count );

		var batch = b.TakeBatch();
		Assert.AreEqual( 1, batch.Count );
		Assert.AreEqual( 0, b.Count );
	}

	[TestMethod]
	public void TakeBatch_RespectsMax()
	{
		var b = new EventBuffer();
		for ( var i = 0; i < 10; i++ )
			b.Add( Ev() );

		var batch = b.TakeBatch( 4 );
		Assert.AreEqual( 4, batch.Count );
		Assert.AreEqual( 6, b.Count );
	}

	[TestMethod]
	public void Add_OversizedProperties_Rejected()
	{
		var b = new EventBuffer();
		var big = new string( 'x', 20 * 1024 );
		Assert.IsFalse( b.Add( Ev( props: new Dictionary<string, object> { ["blob"] = big } ) ) );
		Assert.AreEqual( 0, b.Count );
	}

	[TestMethod]
	public void Add_OverCapacity_DropsOldest()
	{
		var b = new EventBuffer( capacity: 3 );
		for ( var i = 0; i < 5; i++ )
			b.Add( Ev( type: i.ToString() ) );

		Assert.AreEqual( 3, b.Count );
		var batch = b.TakeBatch();
		// oldest two (0,1) dropped; remaining are 2,3,4
		Assert.AreEqual( "2", batch[0].Type );
	}
}
