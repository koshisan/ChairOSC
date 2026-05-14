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
    private readonly Dictionary<string, VelocityCalculator> _calc = new();
    private readonly Dictionary<int, double> _lastSent = new();
    private readonly Dictionary<int, long> _lastSentMs = new();
    private bool _lastHeat;
    public event Action<string>? Log;
    public event Action<int, double>? IntensityChanged;  // (hwZone 1..4, intensity 0..1)
    public event Action<bool>? HeatChanged;

    private static readonly string[] OscZones = { "back", "lumbar", "lthigh", "rthigh", "lleg", "rleg" };

    public ZoneController(AppConfig cfg, EspClient esp)
    {
        Cfg = cfg;
        _esp = esp;
        foreach (var z in OscZones) _calc[z] = new VelocityCalculator(cfg.VelocityWindowMs, cfg.MaxRealisticVelocity);
        for (int i = 1; i <= 4; i++) { _lastSent[i] = -1; _lastSentMs[i] = 0; }
    }

    public void RebuildCalculators()
    {
        foreach (var z in OscZones) _calc[z] = new VelocityCalculator(Cfg.VelocityWindowMs, Cfg.MaxRealisticVelocity);
    }

    /// <summary>Called when an OSC ChairOSC/v1/{zone} float arrives.</summary>
    public async Task OnZoneAsync(string zoneName, double proximity)
    {
        if (!_calc.TryGetValue(zoneName, out var calc)) return;
        calc.Push(Math.Clamp(proximity, 0.0, 1.0));

        if (!Cfg.Enabled) return;

        // Recompute intensities for ALL hardware zones, because a single OSC update may
        // affect a hardware zone that shares with another OSC zone (max-aggregation).
        var hwIntensities = new Dictionary<int, double>();
        for (int hw = 1; hw <= 4; hw++) hwIntensities[hw] = 0.0;

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

        var now = Environment.TickCount64;
        foreach (var (hw, val) in hwIntensities)
        {
            // Skip if unchanged enough or sent too recently
            if (Math.Abs(val - _lastSent[hw]) < 0.02 && val != 0.0) continue;
            if (now - _lastSentMs[hw] < Cfg.EspMinUpdateIntervalMs) continue;
            _lastSent[hw] = val;
            _lastSentMs[hw] = now;
            IntensityChanged?.Invoke(hw, val);
            var ok = await _esp.SetIntensityAsync(hw, val).ConfigureAwait(false);
            if (!ok) Log?.Invoke($"ESP zone {hw} set {val:0.00} FAILED");
        }
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
