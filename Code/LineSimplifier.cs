using System.Collections.Generic;
using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Ramer–Douglas–Peucker polyline simplification. Drops points that lie within
/// <c>epsilon</c> units of the straight line between the kept points, preserving
/// endpoints and corners. Trims a dense trajectory down to the vertices that
/// actually describe its shape, so a flush ships far fewer points than were
/// sampled.
/// </summary>
public static class LineSimplifier
{
	/// <summary>
	/// Simplified copy of <paramref name="points"/> in original order. Inputs of
	/// two or fewer points are returned unchanged.
	/// </summary>
	public static List<Vector3> Simplify( IReadOnlyList<Vector3> points, float epsilon )
	{
		if ( points is null || points.Count <= 2 )
			return points is null ? new List<Vector3>() : new List<Vector3>( points );

		var keep = new bool[points.Count];
		keep[0] = true;
		keep[^1] = true;
		SimplifySegment( points, 0, points.Count - 1, epsilon, keep );

		var result = new List<Vector3>();
		for ( var i = 0; i < points.Count; i++ )
		{
			if ( keep[i] )
				result.Add( points[i] );
		}

		return result;
	}

	// Recursively keep the farthest point from the [first, last] segment whenever
	// it exceeds epsilon, then recurse into the two halves it splits.
	static void SimplifySegment( IReadOnlyList<Vector3> points, int first, int last, float epsilon, bool[] keep )
	{
		if ( last <= first + 1 )
			return;

		var maxDistance = 0f;
		var maxIndex = first;
		for ( var i = first + 1; i < last; i++ )
		{
			var distance = PerpendicularDistance( points[i], points[first], points[last] );
			if ( distance > maxDistance )
			{
				maxDistance = distance;
				maxIndex = i;
			}
		}

		if ( maxDistance <= epsilon )
			return;

		keep[maxIndex] = true;
		SimplifySegment( points, first, maxIndex, epsilon, keep );
		SimplifySegment( points, maxIndex, last, epsilon, keep );
	}

	// Distance from point to the line through a→b. Degenerate segment (a == b)
	// falls back to the distance from the shared endpoint.
	static float PerpendicularDistance( Vector3 point, Vector3 a, Vector3 b )
	{
		var ab = b - a;
		var lengthSquared = ab.LengthSquared;
		if ( lengthSquared <= 0f )
			return point.Distance( a );

		// Project point onto the infinite line, clamp nowhere — RDP measures the
		// perpendicular offset, and t outside [0,1] only happens at the recursion
		// endpoints which are already pinned.
		var t = Vector3.Dot( point - a, ab ) / lengthSquared;
		var projection = a + ab * t;
		return point.Distance( projection );
	}
}
