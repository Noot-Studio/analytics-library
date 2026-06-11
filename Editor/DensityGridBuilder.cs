using System;
using System.Collections.Generic;

namespace Noot.Analytics.Editor;

/// <summary>One aggregated voxel from GET /v1/spatial/voxels. Coordinates are world-space voxel centers.</summary>
public record struct HeatmapVoxel( float X, float Y, float Z, float Count, float? Value );

/// <summary>A normalized R8 3D density field plus the world-space box it maps onto.</summary>
public sealed record DensityGrid(
	byte[] Data,
	int Nx, int Ny, int Nz,
	Vector3 Center,
	Vector3 Size );

/// <summary>
/// C# port of the web dashboard's buildDensityGrid (fog-volume-canvas.tsx):
/// splat voxel intensities into a float grid, blur, normalize against the 95th
/// percentile of occupied cells, quantize to bytes. Pure — unit-testable.
/// s&amp;box is Z-up natively, so unlike the Three.js (Y-up) version there is no
/// axis swizzle: grid axes map x→x, y→y, z→z.
/// </summary>
public static class DensityGridBuilder
{
	// Cap total grid cells so a tiny voxelSize over a large scene can't
	// allocate a multi-hundred-MB texture or stall the blur passes.
	public const int MaxGridCells = 2_000_000;

	const int BlurPasses = 2;
	const float NormalizePercentile = 0.95f;
	const int Uint8Max = 255;

	/// <summary>Returns null for an empty voxel set or when the grid would exceed <see cref="MaxGridCells"/>.</summary>
	public static DensityGrid Build( IReadOnlyList<HeatmapVoxel> voxels, float voxelSize, bool useMetric )
	{
		if ( voxels == null || voxels.Count == 0 )
			return null;

		float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
		float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;

		foreach ( var v in voxels )
		{
			minX = MathF.Min( minX, v.X );
			minY = MathF.Min( minY, v.Y );
			minZ = MathF.Min( minZ, v.Z );
			maxX = MathF.Max( maxX, v.X );
			maxY = MathF.Max( maxY, v.Y );
			maxZ = MathF.Max( maxZ, v.Z );
		}

		static int Cells( float min, float max, float voxelSize ) =>
			Math.Max( 1, (int)MathF.Round( (max - min) / voxelSize ) + 1 );

		var nx = Cells( minX, maxX, voxelSize );
		var ny = Cells( minY, maxY, voxelSize );
		var nz = Cells( minZ, maxZ, voxelSize );

		if ( (long)nx * ny * nz > MaxGridCells )
			return null;

		int Idx( int x, int y, int z ) => x + nx * (y + ny * z);

		var field = new float[nx * ny * nz];
		var maxIntensity = 0f;
		foreach ( var v in voxels )
		{
			var ix = (int)MathF.Round( (v.X - minX) / voxelSize );
			var iy = (int)MathF.Round( (v.Y - minY) / voxelSize );
			var iz = (int)MathF.Round( (v.Z - minZ) / voxelSize );
			var intensity = useMetric ? (v.Value ?? 0f) : v.Count;
			var at = Idx( ix, iy, iz );
			field[at] += intensity;
			maxIntensity = MathF.Max( maxIntensity, field[at] );
		}

		var scratch = new float[field.Length];
		for ( var pass = 0; pass < BlurPasses; pass++ )
		{
			BlurAxis( field, scratch, nx, ny, nz, 0 );
			BlurAxis( scratch, field, nx, ny, nz, 1 );
			BlurAxis( field, scratch, nx, ny, nz, 2 );
			(field, scratch) = (scratch, field);
		}

		// Normalize against the 95th percentile of occupied cells, not the
		// single peak. One outlier voxel otherwise rescales the whole field and
		// washes it flat; cells above the percentile clamp to 255 below.
		var occupied = new List<float>();
		foreach ( var value in field )
		{
			if ( value > 0f )
				occupied.Add( value );
		}
		occupied.Sort();
		var percentileValue = occupied.Count > 0
			? occupied[Math.Min( occupied.Count - 1, (int)MathF.Floor( occupied.Count * NormalizePercentile ) )]
			: maxIntensity;
		var scale = percentileValue > 0f ? percentileValue : maxIntensity;
		var norm = scale > 0f ? Uint8Max / scale : 0f;

		var data = new byte[field.Length];
		for ( var i = 0; i < field.Length; i++ )
			data[i] = (byte)Math.Min( Uint8Max, (int)MathF.Round( field[i] * norm ) );

		// Voxel coords are cell centers, so the box pads half a voxel on every
		// side: [min - vs/2, max + vs/2]. That puts each voxel center on a texel
		// center ((i + 0.5) / n) — the web port added an extra half-voxel offset
		// here, which shifted the fog off the cube/world positions.
		var size = new Vector3( maxX - minX + voxelSize, maxY - minY + voxelSize, maxZ - minZ + voxelSize );
		var center = new Vector3(
			(minX + maxX) / 2f,
			(minY + maxY) / 2f,
			(minZ + maxZ) / 2f );

		return new DensityGrid( data, nx, ny, nz, center, size );
	}

	// Separable 3-tap [1,2,1] blur along one grid axis. Run per axis to
	// diffuse discrete voxel splats into a continuous fog field.
	static void BlurAxis( float[] src, float[] dst, int nx, int ny, int nz, int axis )
	{
		int Idx( int x, int y, int z ) => x + nx * (y + ny * z);

		for ( var z = 0; z < nz; z++ )
		{
			for ( var y = 0; y < ny; y++ )
			{
				for ( var x = 0; x < nx; x++ )
				{
					var here = Idx( x, y, z );
					int prev, next;
					if ( axis == 0 )
					{
						prev = Idx( Math.Max( x - 1, 0 ), y, z );
						next = Idx( Math.Min( x + 1, nx - 1 ), y, z );
					}
					else if ( axis == 1 )
					{
						prev = Idx( x, Math.Max( y - 1, 0 ), z );
						next = Idx( x, Math.Min( y + 1, ny - 1 ), z );
					}
					else
					{
						prev = Idx( x, y, Math.Max( z - 1, 0 ) );
						next = Idx( x, y, Math.Min( z + 1, nz - 1 ) );
					}
					dst[here] = (src[prev] + 2f * src[here] + src[next]) / 4f;
				}
			}
		}
	}
}
