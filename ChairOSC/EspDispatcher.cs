namespace ChairOSC;

/// <summary>
/// Decouples OSC packet processing from ESP HTTP writes.
///
/// Producers (ZoneController) call Set(hw, value) — this only updates an
/// in-memory "latest pending" slot per hardware zone, never blocks. A
/// background loop drains the slots at the throttle interval and sends
/// the most-recent value to the ESP via HttpClient.
///
/// Why this matters: VRChat fires many OSC packets per frame; if HTTP
/// writes happen on the OSC receive thread, the OS UDP socket buffer can
/// overflow and *drop packets*, including the all-important "back to 0"
/// event at the end of a contact gesture. With this design, the OSC
/// receive thread does only CPU work (parse + compute intensity) and
/// the HTTP path runs on its own task.
///
/// "Latest value wins" semantics: throttling never silently loses the
/// most recent state. If three updates land in one throttle window, the
/// dispatcher sends the third one when the window opens.
/// </summary>
public class EspDispatcher : IDisposable
{
    private readonly EspClient _esp;
    private readonly Func<int> _intervalMs;
    private readonly double[] _pending = new double[4];
    private readonly double[] _lastSent = new double[4];
    private readonly bool[] _hasPending = new bool[4];
    private readonly object _lock = new();
    private Task? _loop;
    private CancellationTokenSource? _cts;

    /// <summary>Fires when a value was actually pushed to the ESP — for UI sync.</summary>
    public event Action<int, double>? Dispatched;
    public event Action<string>? Log;

    public EspDispatcher(EspClient esp, Func<int> intervalMs)
    {
        _esp = esp;
        _intervalMs = intervalMs;
        for (int i = 0; i < 4; i++) _lastSent[i] = -1;
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _loop?.Wait(500); } catch { }
        _loop = null;
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>Queue a value for dispatch. Always overwrites pending — latest wins.</summary>
    public void Set(int hwZone, double value)
    {
        if (hwZone < 1 || hwZone > 4) return;
        lock (_lock)
        {
            _pending[hwZone - 1] = value;
            _hasPending[hwZone - 1] = true;
        }
    }

    /// <summary>
    /// Reset internal "lastSent" state — used by the Test tab so an
    /// out-of-band ESP write doesn't get suppressed as a duplicate.
    /// </summary>
    public void NotifyExternalWrite(int hwZone, double value)
    {
        if (hwZone < 1 || hwZone > 4) return;
        lock (_lock) { _lastSent[hwZone - 1] = value; }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            for (int hw = 1; hw <= 4; hw++)
            {
                double val;
                bool shouldDispatch;
                lock (_lock)
                {
                    if (!_hasPending[hw - 1]) continue;
                    val = _pending[hw - 1];
                    bool changed = Math.Abs(val - _lastSent[hw - 1]) >= 0.02;
                    bool goingToZero = val == 0.0 && _lastSent[hw - 1] != 0.0;
                    shouldDispatch = changed || goingToZero;
                    _hasPending[hw - 1] = false;       // consumed
                    if (shouldDispatch) _lastSent[hw - 1] = val;
                }
                if (shouldDispatch)
                {
                    Dispatched?.Invoke(hw, val);
                    var ok = await _esp.SetIntensityAsync(hw, val).ConfigureAwait(false);
                    if (!ok) Log?.Invoke($"ESP zone {hw} set {val:0.00} FAILED");
                }
            }

            try { await Task.Delay(Math.Max(10, _intervalMs()), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    public void Dispose() => Stop();
}
