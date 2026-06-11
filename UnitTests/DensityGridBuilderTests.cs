using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Noot.Analytics.Editor;

namespace Noot.Analytics.UnitTests;

[TestClass]
public class DensityGridBuilderTests
{
	[TestMethod]
	public void EmptyVoxelSet_ReturnsNull()
	{
		Assert.IsNull( DensityGridBuilder.Build( new List<HeatmapVoxel>(), 64f, false ) );
		Assert.IsNull( DensityGridBuilder.Build( null!, 64f, false ) );
	}

	[TestMethod]
	public void SingleVoxel_SplatsAtCenterOfOneCellGrid()
	{
		var voxels = new List<HeatmapVoxel> { new( 32f, 32f, 32f, 10f, null ) };
		var grid = DensityGridBuilder.Build( voxels, 64f, false );

		Assert.IsNotNull( grid );
		Assert.AreEqual( 1, grid.Nx );
		Assert.AreEqual( 1, grid.Ny );
		Assert.AreEqual( 1, grid.Nz );
		// One cell, clamped blur leaves it untouched, normalizes to full byte.
		Assert.AreEqual( 255, grid.Data[0] );
		// Box pads the cell center by half a voxel each side: a single voxel
		// centered at 32 with size 64 fills exactly [0, 64].
		Assert.AreEqual( 64f, grid.Size.x, 0.001f );
		Assert.AreEqual( 64f, grid.Size.y, 0.001f );
		Assert.AreEqual( 64f, grid.Size.z, 0.001f );
		Assert.AreEqual( 32f, grid.Center.x, 0.001f );
		Assert.AreEqual( 32f, grid.Center.y, 0.001f );
		Assert.AreEqual( 32f, grid.Center.z, 0.001f );
	}

	[TestMethod]
	public void NoAxisSwizzle_GridDimsFollowWorldAxes()
	{
		// 3 voxels along world Y only → ny = 3, nx = nz = 1 (Z-up: no swap).
		var voxels = new List<HeatmapVoxel>
		{
			new( 32f, 32f, 32f, 1f, null ),
			new( 32f, 96f, 32f, 1f, null ),
			new( 32f, 160f, 32f, 1f, null ),
		};
		var grid = DensityGridBuilder.Build( voxels, 64f, false );

		Assert.IsNotNull( grid );
		Assert.AreEqual( 1, grid.Nx );
		Assert.AreEqual( 3, grid.Ny );
		Assert.AreEqual( 1, grid.Nz );
	}

	[TestMethod]
	public void Blur_DiffusesIntoNeighborCells()
	{
		// One hot voxel in the middle of a 3-cell line: after blurring, the
		// neighbors must hold density too, and less than the center.
		var voxels = new List<HeatmapVoxel>
		{
			new( 32f, 32f, 32f, 0f, null ),
			new( 96f, 32f, 32f, 100f, null ),
			new( 160f, 32f, 32f, 0f, null ),
		};
		var grid = DensityGridBuilder.Build( voxels, 64f, false );

		Assert.IsNotNull( grid );
		Assert.AreEqual( 3, grid.Nx );
		Assert.IsTrue( grid.Data[0] > 0, "left neighbor should receive blurred density" );
		Assert.IsTrue( grid.Data[2] > 0, "right neighbor should receive blurred density" );
		Assert.IsTrue( grid.Data[1] > grid.Data[0], "center must stay hottest" );
	}

	[TestMethod]
	public void Normalization_OutlierClampsInsteadOfWashingFieldFlat()
	{
		// Many equal cells plus one extreme outlier, spread on a line. With
		// 95th-percentile normalization the ordinary cells stay visibly hot;
		// with max normalization they would crush toward zero.
		var voxels = new List<HeatmapVoxel>();
		for ( var i = 0; i < 30; i++ )
			voxels.Add( new HeatmapVoxel( 32f + 64f * i * 3, 32f, 32f, 10f, null ) );
		voxels.Add( new HeatmapVoxel( 32f + 64f * 30 * 3, 32f, 32f, 10_000f, null ) );

		var grid = DensityGridBuilder.Build( voxels, 64f, false );

		Assert.IsNotNull( grid );
		// Outlier cell clamps to 255.
		var last = grid.Data[grid.Nx - 1];
		Assert.AreEqual( 255, last );
		// First ordinary splat stays clearly above the noise floor.
		Assert.IsTrue( grid.Data[0] > 100, $"ordinary cell washed out: {grid.Data[0]}" );
	}

	[TestMethod]
	public void MetricMode_UsesValueInsteadOfCount()
	{
		var voxels = new List<HeatmapVoxel>
		{
			new( 32f, 32f, 32f, 1000f, 1f ),
			new( 224f, 32f, 32f, 1f, 100f ),
		};
		var grid = DensityGridBuilder.Build( voxels, 64f, true );

		Assert.IsNotNull( grid );
		// In metric mode the second voxel (value 100) is the hot one despite
		// its tiny count.
		Assert.IsTrue( grid.Data[grid.Nx - 1] > grid.Data[0] );
	}

	[TestMethod]
	public void CellCap_RefusesOversizedGrid()
	{
		// Two voxels far enough apart that the grid would exceed the cap.
		var voxels = new List<HeatmapVoxel>
		{
			new( 0f, 0f, 0f, 1f, null ),
			new( 200f * 200f, 200f * 1f, 200f * 51f, 1f, null ),
		};
		// dims ≈ 201 x 2 x 52 with voxelSize 200 → 20,904 cells (fine); shrink
		// voxel size to 1 → 40001 x 201 x 10201 → way past the cap.
		Assert.IsNotNull( DensityGridBuilder.Build( voxels, 200f, false ) );
		Assert.IsNull( DensityGridBuilder.Build( voxels, 1f, false ) );
	}
}
