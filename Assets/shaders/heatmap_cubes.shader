// Unlit translucent vertex-color shader for the heatmap's instanced-cube
// fallback mode. HeatmapOverlay bakes the inferno ramp + density alpha into
// per-vertex colors of one combined mesh; this just passes them through.

HEADER
{
	Description "Analytics heatmap voxel cubes (unlit vertex color)";
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
	float4 vColor : COLOR0 < Semantic( Color ); >;
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
		// ProcessVertex does not carry COLOR0 through; copy it ourselves or
		// the pixel shader reads an uninitialized (gray) vertex color.
		i.vVertexColor = v.vColor;
		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"

	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		return i.vVertexColor;
	}
}
