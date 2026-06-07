using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Sandbox;

namespace Noot.Analytics;

/// <summary>Real transport: POST {ingestUrl}/v1/events with the x-api-key header.</summary>
public sealed class HttpEventSender : IEventSender
{
	public async Task<bool> SendAsync( List<AnalyticsEvent> batch, string apiKey, string ingestUrl )
	{
		var events = new List<Dictionary<string, object>>( batch.Count );
		foreach ( var ev in batch )
			events.Add( ev.ToPayload() );

		var json = Json.Serialize( new Dictionary<string, object> { ["events"] = events } );
		var content = new StringContent( json, Encoding.UTF8, "application/json" );
		var headers = new Dictionary<string, string> { ["x-api-key"] = apiKey };

		try
		{
			var response = await Http.RequestAsync( $"{ingestUrl.TrimEnd( '/' )}/v1/events", "POST", content, headers );
			return response.StatusCode == HttpStatusCode.Accepted;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Analytics] send failed: {e.Message}" );
			return false;
		}
	}
}
