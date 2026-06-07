using System;
using System.Collections.Generic;
using Sandbox;

namespace Noot.Analytics;

/// <summary>One analytics event. Immutable; <see cref="ToPayload"/> produces the wire shape.</summary>
public sealed class AnalyticsEvent
{
	public required string Type { get; init; }
	public required string SessionId { get; init; }
	public string PlayerId { get; init; } = "";
	public object? Properties { get; init; }
	public DateTime Timestamp { get; init; }
	public string Scene { get; init; } = "";
	public Vector3? Position { get; init; }

	/// <summary>
	/// Build the exact wire object. Keys are literal snake_case strings, so JSON
	/// serialization can never drift from the ingest contract. Empty optionals omitted.
	/// </summary>
	public Dictionary<string, object> ToPayload()
	{
		var p = new Dictionary<string, object>
		{
			["type"] = Type,
			["session_id"] = SessionId,
			["timestamp"] = Timestamp.ToUniversalTime().ToString( "o" ),
		};

		if ( !string.IsNullOrEmpty( PlayerId ) )
			p["player_id"] = PlayerId;

		if ( Properties is not null )
			p["properties"] = Properties;

		if ( !string.IsNullOrEmpty( Scene ) )
			p["scene"] = Scene;

		if ( Position is { } pos )
			p["position"] = new Dictionary<string, object> { ["x"] = pos.x, ["y"] = pos.y, ["z"] = pos.z };

		return p;
	}
}
