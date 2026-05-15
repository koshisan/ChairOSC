namespace ChairOSC;

/// <summary>
/// Combines OSC zone inputs (back/lumbar/lthigh/rthigh/lleg/rleg/heat) into
/// hardware-zone (1..4) intensity values and dispatches them to the ESP.
/// Aggregation per hardware zone = MAX over contributing OSC zones, so two
/// active OSC zones (e.g. lthigh+rthigh) don't cancel each other.
/// </summary>
public class ZoneController
{
    public AppConfig Cfg { get; set; }
    private readonly EspClient _esp;
    private readonly EspDispatcher _dispatcher;
    private readonly Dictionary<string, VelocityCalculator> _calc = new();
    private bool _lastHeat;
    public event Action<string>? Log;
    public event Action<int, double>? IntensityChanged;  // (hwZone 1..4, intensity 0..1)
    public event Action<bool>? HeatChanged;

    private static readonly string[] OscZones = { "back", "lumbar", "lthigh", "rthigh", "lleg", "rleg" };

    public ZoneController(AppConfig cfg, EspClient esp, EspDispatcher dispatcher)
    {
        Cfg = cfg;
        _esp = esp;
        _dispatcher = dispatcher;
        foreach (var z in OscZones) _calc[z] = new VelocityCalculator(cfg.VelocityWindowMs);
        // Re-fire the dispatcher's "actually sent" event as IntensityChanged so the
        // UI matches what the ESP received (not what was queued).
        _dispatcher.Dispatched += (hw, val) => IntensityChanged?.Invoke(hw, val);
        _dispatcher.Log += msg => Log?.Invoke(msg);
    }

    public void RebuildCalculators()
    {
        foreach (var z in OscZones) _calc[z] = new VelocityCalculator(Cfg.VelocityWindowMs);
    }

    /// <summary>
    /// Called for every OSC ChairOSC/v1/{zone} packet. CPU-only path — never
    /// blocks on HTTP. The actual ESP write happens on the dispatcher loop so
    /// the OSC receive thread can keep draining UDP packets without dropping.
    /// </summary>
    public Task OnZoneAsync(string zoneName, double proximity)
    {
        if (!_calc.TryGetValue(zoneName, out var calc)) return Task.CompletedTask;
        calc.Push(Math.Clamp(proximity, 0.0, 1.0));

        if (!Cfg.Enabled) return Task.CompletedTask;

        // Recompute intensities for ALL 4 hardware zones, because one OSC update may
        // affect a hardware zone that shares with another OSC zone (max-aggregation).
        var hwIntensities = new double[5];  // index 0 unused
        foreach (var z in OscZones)
        {
            var (hw, mult) = MapZone(z);
            if (hw < 1 || hw > 4) continue;

            var cur = _calc[z].LastValue;
            var vel = _calc[z].MaxVelocity();

            double intensity = 0.0;
            if (cur > Cfg.TouchThreshold)
            {
                intensity = Math.Max(Cfg.BaseIntensity, Cfg.VelocityScale * vel);
            }
            intensity *= mult;
            intensity = Math.Clamp(intensity, 0.0, Cfg.MaxIntensity);

            if (intensity > hwIntensities[hw]) hwIntensities[hw] = intensity;
        }

        // Hand each hw zone's latest target to the dispatcher. "Latest wins":
        // if multiple updates land in one throttle window, only the most-recent
        // value is sent — but it WILL be sent, never silently dropped.
        for (int hw = 1; hw <= 4; hw++)
        {
            _dispatcher.Set(hw, hwIntensities[hw]);
        }
        return Task.CompletedTask;
    }

    public async Task OnHeatAsync(bool on)
    {
        if (!Cfg.Enabled) return;
        if (on == _lastHeat) return;
        _lastHeat = on;
        HeatChanged?.Invoke(on);
        var ok = await _esp.SetSwitchAsync(Cfg.HeatEntity, on).ConfigureAwait(false);
        Log?.Invoke($"Heat → {(on ? "on" : "off")} {(ok ? "" : "(failed)")}");
    }

    private (int hw, double mult) MapZone(string zone) => zone switch
    {
        "back"   => (Cfg.HwZoneBack,   Cfg.MultBack),
        "lumbar" => (Cfg.HwZoneLumbar, Cfg.MultLumbar),
        "lthigh" => (Cfg.HwZoneLThigh, Cfg.MultLThigh),
        "rthigh" => (Cfg.HwZoneRThigh, Cfg.MultRThigh),
        "lleg"   => (Cfg.HwZoneLLeg,   Cfg.MultLLeg),
        "rleg"   => (Cfg.HwZoneRLeg,   Cfg.MultRLeg),
        _ => (0, 0.0),
    };
}
