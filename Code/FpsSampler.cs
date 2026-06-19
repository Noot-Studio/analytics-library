namespace Noot.Analytics;

/// <summary>
/// Pure fixed-window frame-rate accumulator: counts rendered frames and the real
/// time they spanned, and once the window closes yields ONE average FPS for the
/// window and rearms. One emitted sample per window bounds traffic — a per-frame
/// firehose would otherwise be one event every 16ms.
///
/// Time is passed in, never read from the engine, so the sampler is deterministic
/// under test — same pattern as <see cref="MovementSampler"/> and
/// <see cref="FlushWindow"/>.
/// </summary>
public sealed class FpsSampler
{
	/// <summary>Smallest allowed window. Below this a window could close almost every frame.</summary>
	public const float MinWindowSeconds = 1f;

	readonly float _windowSeconds;
	int _frames;
	double _elapsed;

	public FpsSampler( float windowSeconds ) =>
		_windowSeconds = windowSeconds < MinWindowSeconds ? MinWindowSeconds : windowSeconds;

	/// <summary>The effective window after the 1s floor.</summary>
	public float WindowSeconds => _windowSeconds;

	/// <summary>
	/// Record one rendered frame of <paramref name="frameDeltaSeconds"/> real time.
	/// Returns the window's average FPS when the window closes (resetting the
	/// accumulator), or null while it is still open. Non-positive deltas are
	/// ignored so a stalled or paused frame can't poison the average.
	/// </summary>
	public float? Tick( double frameDeltaSeconds )
	{
		if ( frameDeltaSeconds <= 0 )
			return null;

		_frames++;
		_elapsed += frameDeltaSeconds;

		if ( _elapsed < _windowSeconds )
			return null;

		var fps = (float)(_frames / _elapsed);
		_frames = 0;
		_elapsed = 0;
		return fps;
	}
}
