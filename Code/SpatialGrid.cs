using System;
using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Pure quantizer: snaps a world <see cref="Vector3"/> to integer cell indices on
/// a uniform grid of <see cref="CellSize"/> units. Floor-based so the cell a
/// position belongs to never depends on sign — (−1, 0, 0) and (1, 0, 0) at cell
/// size 64 land in cells −1 and 0, not both in 0.
/// </summary>
public sealed class SpatialGrid
{
	/// <summary>Edge length (units) of one cubic cell. Floored to a small positive value.</summary>
	public float CellSize { get; }

	public SpatialGrid( float cellSize )
	{
		// Guard against a zero/negative inspector field turning quantization into a
		// divide-by-zero or sign flip.
		CellSize = cellSize < 1f ? 1f : cellSize;
	}

	/// <summary>Integer cell the position falls into, by flooring each axis.</summary>
	public (int gx, int gy, int gz) Cell( Vector3 pos ) => (
		(int)MathF.Floor( pos.x / CellSize ),
		(int)MathF.Floor( pos.y / CellSize ),
		(int)MathF.Floor( pos.z / CellSize )
	);
}
