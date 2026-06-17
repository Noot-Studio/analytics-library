using System.Collections.Generic;
using Noot.Analytics;

[TestClass]
public class LineSimplifierTests
{
	[TestMethod]
	public void TwoOrFewerPoints_ReturnedUnchanged()
	{
		var single = new List<Vector3> { new( 1f, 2f, 3f ) };
		var pair = new List<Vector3> { Vector3.Zero, new( 100f, 0f, 0f ) };

		Assert.AreEqual( 1, LineSimplifier.Simplify( single, 10f ).Count );
		Assert.AreEqual( 2, LineSimplifier.Simplify( pair, 10f ).Count );
	}

	[TestMethod]
	public void CollinearPoints_DropMiddleKeepEndpoints()
	{
		// Five points on a perfectly straight line: only the two endpoints survive.
		var points = new List<Vector3>
		{
			new( 0f, 0f, 0f ),
			new( 25f, 0f, 0f ),
			new( 50f, 0f, 0f ),
			new( 75f, 0f, 0f ),
			new( 100f, 0f, 0f ),
		};

		var simplified = LineSimplifier.Simplify( points, 1f );

		Assert.AreEqual( 2, simplified.Count );
		Assert.AreEqual( points[0], simplified[0] );
		Assert.AreEqual( points[^1], simplified[^1] );
	}

	[TestMethod]
	public void Corner_IsKept()
	{
		// An L-shape: the corner is 100 units off the start→end diagonal, far past
		// epsilon, so it must be retained.
		var points = new List<Vector3>
		{
			new( 0f, 0f, 0f ),
			new( 100f, 0f, 0f ),
			new( 100f, 100f, 0f ),
		};

		var simplified = LineSimplifier.Simplify( points, 10f );

		Assert.AreEqual( 3, simplified.Count );
		Assert.AreEqual( new Vector3( 100f, 0f, 0f ), simplified[1] );
	}

	[TestMethod]
	public void NearCollinearWithinEpsilon_IsDropped()
	{
		// Middle point sits 5 units off the line; epsilon 10 → dropped.
		var points = new List<Vector3>
		{
			new( 0f, 0f, 0f ),
			new( 50f, 5f, 0f ),
			new( 100f, 0f, 0f ),
		};

		var simplified = LineSimplifier.Simplify( points, 10f );

		Assert.AreEqual( 2, simplified.Count );
	}

	[TestMethod]
	public void DeviationBeyondEpsilon_IsKept()
	{
		// Same shape, middle point 20 units off the line; epsilon 10 → kept.
		var points = new List<Vector3>
		{
			new( 0f, 0f, 0f ),
			new( 50f, 20f, 0f ),
			new( 100f, 0f, 0f ),
		};

		var simplified = LineSimplifier.Simplify( points, 10f );

		Assert.AreEqual( 3, simplified.Count );
	}

	[TestMethod]
	public void EndpointsAlwaysPreserved()
	{
		var points = new List<Vector3>
		{
			new( 0f, 0f, 0f ),
			new( 10f, 1f, 0f ),
			new( 20f, 0f, 0f ),
			new( 30f, 1f, 0f ),
			new( 40f, 0f, 0f ),
		};

		// Huge epsilon collapses everything but the two endpoints.
		var simplified = LineSimplifier.Simplify( points, 1000f );

		Assert.AreEqual( 2, simplified.Count );
		Assert.AreEqual( points[0], simplified[0] );
		Assert.AreEqual( points[^1], simplified[^1] );
	}
}
