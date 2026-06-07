using System;
using System.Collections.Generic;
using Noot.Analytics;
using Sandbox;

[TestClass]
public class AnalyticsEventTests
{
	[TestMethod]
	public void ToPayload_RequiredKeys_AlwaysPresent()
	{
		var ev = new AnalyticsEvent
		{
			Type = "level_complete",
			SessionId = "sess_1",
			Timestamp = new DateTime( 2026, 5, 6, 12, 0, 0, DateTimeKind.Utc ),
		};

		var p = ev.ToPayload();

		Assert.AreEqual( "level_complete", p["type"] );
		Assert.AreEqual( "sess_1", p["session_id"] );
		Assert.IsTrue( p.ContainsKey( "timestamp" ) );
	}

	[TestMethod]
	public void ToPayload_EmptyOptionals_Omitted()
	{
		var ev = new AnalyticsEvent { Type = "t", SessionId = "s", Timestamp = DateTime.UtcNow };
		var p = ev.ToPayload();

		Assert.IsFalse( p.ContainsKey( "player_id" ) );
		Assert.IsFalse( p.ContainsKey( "scene" ) );
		Assert.IsFalse( p.ContainsKey( "properties" ) );
		Assert.IsFalse( p.ContainsKey( "position" ) );
	}

	[TestMethod]
	public void ToPayload_SetOptionals_Included()
	{
		var ev = new AnalyticsEvent
		{
			Type = "t",
			SessionId = "s",
			PlayerId = "anon123",
			Scene = "de_dust2",
			Properties = new Dictionary<string, object> { ["k"] = 1 },
			Position = new Vector3( 1f, 2f, 3f ),
			Timestamp = DateTime.UtcNow,
		};

		var p = ev.ToPayload();

		Assert.AreEqual( "anon123", p["player_id"] );
		Assert.AreEqual( "de_dust2", p["scene"] );
		Assert.IsTrue( p.ContainsKey( "properties" ) );
		var pos = (Dictionary<string, object>)p["position"];
		Assert.AreEqual( 1f, pos["x"] );
		Assert.AreEqual( 2f, pos["y"] );
		Assert.AreEqual( 3f, pos["z"] );
	}
}
