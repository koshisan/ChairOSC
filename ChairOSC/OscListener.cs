using System.Net;
using Rug.Osc;

namespace ChairOSC;

/// <summary>
/// Listens for VRChat OSC packets on a UDP port and routes
/// `/avatar/parameters/ChairOSC/v1/{zone}` to the ZoneController.
/// </summary>
public class OscListener : IDisposable
{
    private OscReceiver? _receiver;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly ZoneController _zc;
    public event Action<string>? Log;
    public event Action<string, double>? RawOsc;  // (path, value)
    public bool IsRunning => _loop is { IsCompleted: false };

    public OscListener(ZoneController zc) { _zc = zc; }

    public void Start(string bindIp, int port)
    {
        Stop();
        _cts = new CancellationTokenSource();
        IPAddress addr = string.IsNullOrEmpty(bindIp) || bindIp == "0.0.0.0"
            ? IPAddress.Any
            : IPAddress.Parse(bindIp);
        _receiver = new OscReceiver(addr, port);
        _receiver.Connect();
        Log?.Invoke($"OSC listening on {addr}:{port}");
        _loop = Task.Run(() => ReceiveLoop(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _receiver?.Close(); } catch { }
        _receiver = null;
        try { _loop?.Wait(500); } catch { }
        _loop = null;
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        if (_receiver == null) return;
        while (!ct.IsCancellationRequested && _receiver.State == OscSocketState.Connected)
        {
            OscPacket? packet;
            try { packet = _receiver.Receive(); }
            catch { if (ct.IsCancellationRequested) return; await Task.Delay(50, ct); continue; }
            if (packet == null) continue;
            await ProcessAsync(packet).ConfigureAwait(false);
        }
    }

    private async Task ProcessAsync(OscPacket packet)
    {
        if (packet is OscBundle bundle)
        {
            foreach (var inner in bundle) await ProcessAsync(inner).ConfigureAwait(false);
            return;
        }
        if (packet is not OscMessage msg) return;
        var addr = msg.Address;

        // VRChat avatar params arrive as /avatar/parameters/ChairOSC/v1/{zone}
        const string prefix = "/avatar/parameters/ChairOSC/v1/";
        if (!addr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
        var zone = addr.Substring(prefix.Length).ToLowerInvariant();
        if (msg.Count == 0) return;
        var arg = msg[0];

        if (zone == "heat")
        {
            // Heat is binary on the recliner ESP, but the avatar parameter
            // can be driven by a proximity contact (float 0..1) so the same
            // path can also feed stepped heat hardware elsewhere. Treat any
            // non-zero positive value as "on" — small epsilon guards against
            // float-zero jitter from VRChat physics.
            double raw = arg switch
            {
                bool b => b ? 1.0 : 0.0,
                int i => i,
                float f => f,
                double d => d,
                _ => 0.0,
            };
            bool on = raw > 0.001;
            RawOsc?.Invoke(addr, raw);
            await _zc.OnHeatAsync(on).ConfigureAwait(false);
            return;
        }

        // back / lumbar / lthigh / rthigh / lleg / rleg → float 0..1
        double v = arg switch
        {
            float f => f,
            double d => d,
            int i => Math.Clamp(i / 100.0, 0.0, 1.0),
            bool b => b ? 1.0 : 0.0,
            _ => 0.0,
        };
        RawOsc?.Invoke(addr, v);
        await _zc.OnZoneAsync(zone, v).ConfigureAwait(false);
    }

    public void Dispose() => Stop();
}
