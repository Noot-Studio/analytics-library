using Noot.Analytics;

[TestClass]
public class SpatialGridTests
{
	[TestMethod]
	public void Origin_IsCellZero()
	{
		var grid = new SpatialGrid( 64f );

		Assert.AreEqual( (0, 0, 0), grid.Cell( Vector3.Zero ) );
	}

	[TestMethod]
	public void PositivePosition_FloorsToCell()
	{
		var grid = new SpatialGrid( 64f );

		// 100 / 64 = 1.5625 → floor 1; 64 is the start of cell 1; 63 still in 0.
		Assert.AreEqual( (1, 0, 0), grid.Cell( new Vector3( 100f, 0f, 0f ) ) );
		Assert.AreEqual( (1, 0, 0), grid.Cell( new Vector3( 64f, 0f, 0f ) ) );
		Assert.AreEqual( (0, 0, 0), grid.Cell( new Vector3( 63f, 0f, 0f ) ) );
	}

	[TestMethod]
	public void NegativePosition_FloorsTowardNegativeInfinity()
	{
		var grid = new SpatialGrid( 64f );

		// −1 / 64 = −0.0156 → floor −1, not 0. The sign must not collapse into cell 0.
		Assert.AreEqual( (-1, -1, -1), grid.Cell( new Vector3( -1f, -1f, -1f ) ) );
		Assert.AreEqual( (-1, 0, 0), grid.Cell( new Vector3( -64f, 0f, 0f ) ) );
		Assert.AreEqual( (-2, 0, 0), grid.Cell( new Vector3( -65f, 0f, 0f ) ) );
	}

	[TestMethod]
	public void QuantizesEachAxisIndependently()
	{
		var grid = new SpatialGrid( 50f );

		Assert.AreEqual( (2, -1, 3), grid.Cell( new Vector3( 120f, -10f, 175f ) ) );
	}

	[TestMethod]
	public void NonPositiveCellSize_IsFlooredToOne()
	{
		var grid = new SpatialGrid( 0f );

		// Floored to 1 unit per cell: position == cell index, no divide-by-zero.
		Assert.AreEqual( 1f, grid.CellSize );
		Assert.AreEqual( (5, -3, 0), grid.Cell( new Vector3( 5.9f, -2.1f, 0.4f ) ) );
	}
}
