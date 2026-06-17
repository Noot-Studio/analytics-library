using System.Collections.Generic;

namespace Noot.Analytics;

/// <summary>
/// Sparse per-cell accumulator. Two independent tallies keyed by integer cell:
/// dwell (milliseconds spent in a cell, summed across ticks) and visits (count of
/// times a cell was entered). Only touched cells consume memory.
///
/// This is the heart of the spatial optimization: many samples fold into one
/// number per cell, so a flush emits a handful of cells instead of one event per
/// sample. <see cref="Drain"/> hands off the accumulated cells and clears them so
/// the next window starts empty.
/// </summary>
public sealed class CellAccumulator
{
	public struct Acc
	{
		public int DwellMs;
		public int Visits;
	}

	readonly Dictionary<(int, int, int), Acc> _cells = new();

	/// <summary>Number of distinct cells touched since the last drain.</summary>
	public int Count => _cells.Count;

	/// <summary>Add elapsed milliseconds to a cell's dwell total.</summary>
	public void AddDwell( (int, int, int) cell, int deltaMs )
	{
		var acc = _cells.GetValueOrDefault( cell );
		acc.DwellMs += deltaMs;
		_cells[cell] = acc;
	}

	/// <summary>Increment a cell's visit count. Call on cell ENTER only.</summary>
	public void AddVisit( (int, int, int) cell )
	{
		var acc = _cells.GetValueOrDefault( cell );
		acc.Visits += 1;
		_cells[cell] = acc;
	}

	/// <summary>Return the accumulated cells and clear, readying the next window.</summary>
	public Dictionary<(int, int, int), Acc> Drain()
	{
		var drained = new Dictionary<(int, int, int), Acc>( _cells );
		_cells.Clear();
		return drained;
	}
}
