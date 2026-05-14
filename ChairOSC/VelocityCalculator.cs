namespace ChairOSC;

/// <summary>
/// Rolling-window velocity estimator for a single OSC zone.
/// Samples are (timestampMs, proximity 0..1).
/// Velocity = max|delta proximity / delta t in seconds| over the last WindowMs.
/// </summary>
public class VelocityCalculator
{
    private readonly Queue<(long t, double v)> _samples = new();
    private readonly int _windowMs;
    private readonly double _maxRealisticVel;
    private double _lastValue;

    public VelocityCalculator(int windowMs, double maxRealisticVel)
    {
        _windowMs = windowMs;
        _maxRealisticVel = maxRealisticVel;
    }

    public void Push(double value)
    {
        var now = Environment.TickCount64;
        _samples.Enqueue((now, value));
        while (_samples.Count > 1 && now - _samples.Peek().t > _windowMs) _samples.Dequeue();
        _lastValue = value;
    }

    public double LastValue => _lastValue;

    /// <summary>
    /// Returns max |dValue/dt| in units-per-second over the window, or 0 if no movement.
    /// Pair-wise velocities above _maxRealisticVel are dropped — VRChat contact
    /// receivers can flicker at sender boundary edges, producing phantom spikes
    /// of 10+ units/sec which would otherwise pin the zone at full intensity.
    /// </summary>
    public double MaxVelocity()
    {
        if (_samples.Count < 2) return 0.0;
        double maxVel = 0.0;
        var arr = _samples.ToArray();
        for (int i = 1; i < arr.Length; i++)
        {
            var dt = (arr[i].t - arr[i - 1].t) / 1000.0; // seconds
            if (dt <= 0) continue;
            var dv = Math.Abs(arr[i].v - arr[i - 1].v);
            var vel = dv / dt;
            if (vel > _maxRealisticVel) continue;  // discard noise-burst spikes
            if (vel > maxVel) maxVel = vel;
        }
        return maxVel;
    }
}
