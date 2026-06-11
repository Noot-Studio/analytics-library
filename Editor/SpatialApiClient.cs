using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Sandbox;

namespace Noot.Analytics.Editor;

/// <summary>Voxels response from GET /v1/spatial/voxels.</summary>
public sealed class VoxelsResponse
{
	[JsonPropertyName( "voxels" )] public List<VoxelDto> Voxels { get; set; } = new();
	[JsonPropertyName( "voxelSize" )] public float VoxelSize { get; set; }
	[JsonPropertyName( "truncated" )] public bool Truncated { get; set; }
}

public sealed class VoxelDto
{
	[JsonPropertyName( "x" )] public float X { get; set; }
	[JsonPropertyName( "y" )] public float Y { get; set; }
	[JsonPropertyName( "z" )] public float Z { get; set; }
	[JsonPropertyName( "count" )] public float Count { get; set; }
	[JsonPropertyName( "value" )] public float? Value { get; set; }
}

/// <summary>Scenes response from GET /v1/spatial/scenes.</summary>
public sealed class ScenesResponse
{
	[JsonPropertyName( "scenes" )] public List<SceneDto> Scenes { get; set; } = new();
}

public sealed class SceneDto
{
	[JsonPropertyName( "scene" )] public string Scene { get; set; } = "";
	[JsonPropertyName( "eventCount" )] public float EventCount { get; set; }
}

/// <summary>Thrown for non-2xx responses; carries the HTTP status for dock error display.</summary>
public sealed class SpatialApiException : Exception
{
	public int StatusCode { get; }

	public SpatialApiException( int statusCode, string message ) : base( message )
	{
		StatusCode = statusCode;
	}
}

/// <summary>
/// Thin async wrapper over the ingest read endpoints. GETs with the x-api-key
/// header; the key never leaves the local editor.
/// </summary>
public sealed class SpatialApiClient
{
	readonly string _ingestUrl;
	readonly string _apiKey;

	public SpatialApiClient( string ingestUrl, string apiKey )
	{
		_ingestUrl = ingestUrl?.TrimEnd( '/' ) ?? "";
		_apiKey = apiKey ?? "";
	}

	public sealed class VoxelsQuery
	{
		public string Scene { get; set; } = "";
		public float VoxelSize { get; set; } = 64f;
		public string From { get; set; } = "";
		public string To { get; set; } = "";
		public string EventType { get; set; }
		public string MetricKey { get; set; }
		public string MetricAgg { get; set; }
	}

	public Task<VoxelsResponse> GetVoxelsAsync( VoxelsQuery q )
	{
		var qs = new List<string>
		{
			$"scene={Uri.EscapeDataString( q.Scene )}",
			$"voxelSize={q.VoxelSize}",
			$"from={Uri.EscapeDataString( q.From )}",
			$"to={Uri.EscapeDataString( q.To )}",
		};
		if ( !string.IsNullOrWhiteSpace( q.EventType ) )
			qs.Add( $"eventType={Uri.EscapeDataString( q.EventType )}" );
		if ( !string.IsNullOrWhiteSpace( q.MetricKey ) )
		{
			qs.Add( $"metricKey={Uri.EscapeDataString( q.MetricKey )}" );
			qs.Add( $"metricAgg={Uri.EscapeDataString( q.MetricAgg ?? "avg" )}" );
		}

		return GetAsync<VoxelsResponse>( $"/v1/spatial/voxels?{string.Join( "&", qs )}" );
	}

	public Task<ScenesResponse> GetScenesAsync( string from, string to ) =>
		GetAsync<ScenesResponse>(
			$"/v1/spatial/scenes?from={Uri.EscapeDataString( from )}&to={Uri.EscapeDataString( to )}" );

	async Task<T> GetAsync<T>( string path )
	{
		var headers = new Dictionary<string, string> { ["x-api-key"] = _apiKey };
		HttpResponseMessage response;
		try
		{
			response = await Http.RequestAsync( $"{_ingestUrl}{path}", "GET", null, headers );
		}
		catch ( Exception e )
		{
			throw new SpatialApiException( 0, $"Request failed: {e.Message}" );
		}

		var body = await response.Content.ReadAsStringAsync();
		if ( !response.IsSuccessStatusCode )
			throw new SpatialApiException( (int)response.StatusCode, $"HTTP {(int)response.StatusCode}" );

		try
		{
			return JsonSerializer.Deserialize<T>( body );
		}
		catch ( JsonException e )
		{
			throw new SpatialApiException( (int)response.StatusCode, $"Bad response body: {e.Message}" );
		}
	}
}
