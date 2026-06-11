using Sandbox;

namespace Noot.Analytics;

/// <summary>
/// Pure throttle for position sampling: emits at most one sample per interval,
/// and only when the subject actually moved. Both gates exist to bound traffic —
/// a stationary entity produces zero events, a moving one at most
/// 1 / interval events per second.
/// </summary>
public sealed class MovementSampler
{
	readonly float _intervalSeconds;
	readonly float _minMoveDistance;

	double _nextSampleTime = double.NegativeInfinity;
	Vector3? _lastPosition;

	public MovementSampler( float intervalSeconds, float minMoveDistance )
	{
		// Floor the interval so a mis-set inspector field can't turn the tracker
		// into a per-frame event firehose.
		_intervalSeconds = intervalSeconds < 0.25f ? 0.25f : intervalSeconds;
		_minMoveDistance = minMoveDistance < 0f ? 0f : minMoveDistance;
	}

	/// <summary>True when a sample should be emitted for this position now.</summary>
	public bool ShouldSample( Vector3 position, double nowSeconds )
	{
		if ( nowSeconds < _nextSampleTime )
			return false;

		// Interval elapsed: schedule the next window regardless of outcome, so a
		// stationary entity re-checks once per interval, not once per frame.
		_nextSampleTime = nowSeconds + _intervalSeconds;

		if ( _lastPosition is { } last && position.Distance( last ) < _minMoveDistance )
			return false;

		_lastPosition = position;
		return true;
	}

	public void Reset()
	{
		_nextSampleTime = double.NegativeInfinity;
		_lastPosition = null;
	}
}
