// Volumetric analytics heatmap. HLSL port of the web dashboard's fog volume
// (fog-volume-canvas.tsx): front-to-back raymarched emission/absorption through
// an R8 density texture, inferno colormap. The mesh is a world-space box built
// by HeatmapOverlay; raymarching happens in world space and maps positions into
// texture coords via VolumeMins/VolumeSize, so no object-space matrix is needed.

HEADER
{
	Description "Analytics volumetric heatmap fog";
}

FEATURES
{
}

MODES
{
	Forward();
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"

	// Translucent: no depth write, render backfaces (the camera may be inside
	// the volume, where front faces are behind the near plane).
	RenderState( CullMode, FRONT );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );

	Texture3D g_tDensity < Attribute( "DensityTexture" ); SrgbRead( false ); >;
	SamplerState g_sDensity < Filter( MIN_MAG_MIP_LINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;

	float3 g_vVolumeMins < Attribute( "VolumeMins" ); >;
	float3 g_vVolumeSize < Attribute( "VolumeSize" ); Default3( 1.0, 1.0, 1.0 ); >;
	float g_flSteps < Attribute( "Steps" ); Default( 96.0 ); >;
	float g_flDensity < Attribute( "Density" ); Default( 3.5 ); >;
	float g_flFalloff < Attribute( "Falloff" ); Default( 1.8 ); >;

	// Inferno colormap polynomial fit. Maps density 0..1 → black → purple →
	// orange → yellow; the near-black low end makes weak voxels vanish instead
	// of fogging the scene one flat color.
	float3 Inferno( float t )
	{
		const float3 c0 = float3( 0.00021894037, 0.0016510046, -0.019480898 );
		const float3 c1 = float3( 0.10651342, 0.56395643, 3.9327124 );
		const float3 c2 = float3( 11.602493, -3.9728540, -15.942394 );
		const float3 c3 = float3( -41.703996, 17.436399, 44.354145 );
		const float3 c4 = float3( 77.162936, -33.402359, -81.807309 );
		const float3 c5 = float3( -71.319428, 32.626064, 73.209520 );
		const float3 c6 = float3( 25.131126, -12.242669, -23.070325 );
		return c0 + t * (c1 + t * (c2 + t * (c3 + t * (c4 + t * (c5 + t * c6)))));
	}

	// Slab test against the world-space volume box. Returns (tNear, tFar).
	float2 HitBox( float3 orig, float3 dir )
	{
		float3 invDir = 1.0 / dir;
		float3 tA = (g_vVolumeMins - orig) * invDir;
		float3 tB = (g_vVolumeMins + g_vVolumeSize - orig) * invDir;
		float3 tMin = min( tA, tB );
		float3 tMax = max( tA, tB );
		float t0 = max( tMin.x, max( tMin.y, tMin.z ) );
		float t1 = min( tMax.x, min( tMax.y, tMax.z ) );
		return float2( t0, t1 );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 rayOrigin = g_vCameraPositionWs;
		float3 rayDir = normalize( i.vPositionWithOffsetWs.xyz );

		float2 bounds = HitBox( rayOrigin, rayDir );
		if ( bounds.x > bounds.y )
			discard;
		bounds.x = max( bounds.x, 0.0 );

		float span = bounds.y - bounds.x;
		// Rejects span <= 0 AND NaN spans (NaN fails every comparison), which a
		// degenerate axis-aligned ray (1.0/dir = inf) can produce.
		if ( !(span > 0.0) )
			discard;

		float delta = span / g_flSteps;
		// The web shader marches a unit cube, so its per-step opacity weight is
		// span/steps with span <= sqrt(3). Here span is in world units; divide
		// by the volume diagonal to keep the same accumulation scale.
		float deltaNorm = delta / length( g_vVolumeSize );

		// Jitter the entry point by up to one step: fixed sample planes alias
		// into concentric banding; per-pixel hash noise averages away.
		float jitter = frac( sin( dot( i.vPositionSs.xy, float2( 12.9898, 78.233 ) ) ) * 43758.5453 );
		float3 p = rayOrigin + (bounds.x + jitter * delta) * rayDir;
		float3 stepVec = rayDir * delta;
		float4 acc = float4( 0.0, 0.0, 0.0, 0.0 );

		// Front-to-back compositing. A constant loop bound is mandatory: a
		// tangent ray can drive delta -> 0 and an open loop would spin forever
		// on the GPU -> watchdog reset. Steps only trims the fixed bound.
		const int MAX_STEPS = 256;
		[loop]
		for ( int s = 0; s < MAX_STEPS; s++ )
		{
			if ( float( s ) >= g_flSteps )
				break;

			float3 uvw = (p - g_vVolumeMins) / g_vVolumeSize;
			float d = g_tDensity.SampleLevel( g_sDensity, uvw, 0 ).r;
			if ( d > 0.001 )
			{
				// clamp() guards the polynomial: it can dip slightly negative
				// near the ends, and pow() NaNs on a negative base.
				float3 c = clamp( Inferno( d ), 0.0, 1.0 );
				float a = clamp( pow( d, g_flFalloff ) * g_flDensity * deltaNorm, 0.0, 1.0 );
				acc.rgb += (1.0 - acc.a) * c * a;
				acc.a += (1.0 - acc.a) * a;
				if ( acc.a >= 0.95 )
					break;
			}
			p += stepVec;
		}

		if ( acc.a <= 0.001 )
			discard;

		// acc.rgb is premultiplied (front-to-back "over"); the SRC_ALPHA blend
		// multiplies by alpha again, so divide back to straight alpha first.
		return float4( acc.rgb / acc.a, acc.a );
	}
}
