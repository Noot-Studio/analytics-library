namespace Noot.Analytics;

/// <summary>
/// Pure fixed-cadence flush timer shared by the spatial trackers
/// (<see cref="AnalyticsDwellComponent"/>, <see cref="AnalyticsHeatmapComponent"/>,
/// <see cref="AnalyticsTrajectoryComponent"/>). Holds the flush interval (floored
/// at 1s so a mis-set inspector field can't flush every frame) and the next-due
/// timestamp: the owning component arms the first window with <see cref="Schedule"/>,
/// asks <see cref="IsDue"/> each tick, and re-arms with <see cref="Schedule"/> after
/// every flush.
///
/// Time is passed in, never read from the engine, so the timer is deterministic
/// under test — same pattern as <see cref="MovementSampler"/>.
/// </summary>
public sealed class FlushWindow
{
	/// <summary>Smallest allowed interval. Below this a window would flush every frame.</summary>
	public const float MinIntervalSeconds = 1f;

	readonly float _intervalSeconds;
	double _nextFlushTime;

	public FlushWindow( float intervalSeconds ) => _intervalSeconds = FloorInterval( intervalSeconds );

	/// <summary>The effective interval after the 1s floor.</summary>
	public float IntervalSeconds => _intervalSeconds;

	/// <summary>Arm the next deadline at <paramref name="nowSeconds"/> + interval.</summary>
	public void Schedule( double nowSeconds ) => _nextFlushTime = nowSeconds + _intervalSeconds;

	/// <summary>True once the armed deadline has passed.</summary>
	public bool IsDue( double nowSeconds ) => nowSeconds >= _nextFlushTime;

	/// <summary>The single definition of the interval floor, shared with the trackers' event estimates.</summary>
	public static float FloorInterval( float intervalSeconds ) =>
		intervalSeconds < MinIntervalSeconds ? MinIntervalSeconds : intervalSeconds;
}
