using Noot.Analytics;

[TestClass]
public class CellAccumulatorTests
{
	[TestMethod]
	public void Dwell_AccumulatesAcrossTicks()
	{
		var acc = new CellAccumulator();

		acc.AddDwell( (1, 2, 3), 16 );
		acc.AddDwell( (1, 2, 3), 16 );
		acc.AddDwell( (1, 2, 3), 8 );

		Assert.AreEqual( 1, acc.Count );
		var drained = acc.Drain();
		Assert.AreEqual( 40, drained[(1, 2, 3)].DwellMs );
	}

	[TestMethod]
	public void Dwell_KeepsCellsSeparate()
	{
		var acc = new CellAccumulator();

		acc.AddDwell( (0, 0, 0), 100 );
		acc.AddDwell( (1, 0, 0), 250 );

		Assert.AreEqual( 2, acc.Count );
		var drained = acc.Drain();
		Assert.AreEqual( 100, drained[(0, 0, 0)].DwellMs );
		Assert.AreEqual( 250, drained[(1, 0, 0)].DwellMs );
	}

	[TestMethod]
	public void Visit_CountsEachCall()
	{
		var acc = new CellAccumulator();

		acc.AddVisit( (5, 5, 5) );
		acc.AddVisit( (5, 5, 5) );
		acc.AddVisit( (9, 9, 9) );

		var drained = acc.Drain();
		Assert.AreEqual( 2, drained[(5, 5, 5)].Visits );
		Assert.AreEqual( 1, drained[(9, 9, 9)].Visits );
	}

	[TestMethod]
	public void DwellAndVisit_TallyIndependentlyOnSameCell()
	{
		var acc = new CellAccumulator();

		acc.AddDwell( (0, 0, 0), 120 );
		acc.AddVisit( (0, 0, 0) );

		Assert.AreEqual( 1, acc.Count );
		var drained = acc.Drain();
		Assert.AreEqual( 120, drained[(0, 0, 0)].DwellMs );
		Assert.AreEqual( 1, drained[(0, 0, 0)].Visits );
	}

	[TestMethod]
	public void Drain_ClearsAccumulator()
	{
		var acc = new CellAccumulator();
		acc.AddVisit( (1, 1, 1) );

		var first = acc.Drain();
		Assert.AreEqual( 1, first.Count );

		// Drained dictionary is a snapshot; the accumulator itself is now empty.
		Assert.AreEqual( 0, acc.Count );
		var second = acc.Drain();
		Assert.AreEqual( 0, second.Count );
	}
}
