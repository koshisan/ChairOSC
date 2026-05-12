using System.Net.Http;

namespace ChairOSC;

/// <summary>
/// Thin HTTP client over ESPHome's web_server REST API.
/// Endpoints used:
///   POST http://{host}/number/{entity}/set?value={float}
///   POST http://{host}/switch/{entity}/turn_on
///   POST http://{host}/switch/{entity}/turn_off
/// </summary>
public class EspClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private string _host;

    public EspClient(string host) { _host = host; }

    public void UpdateHost(string host) => _host = host;

    public async Task<bool> SetIntensityAsync(int hwZone, double value, CancellationToken ct = default)
    {
        var entity = string.Format("recliner3_massage_zone_{0}_intensity", hwZone);
        var url = $"http://{_host}/number/{entity}/set?value={value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}";
        try
        {
            using var resp = await _http.PostAsync(url, null, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> SetSwitchAsync(string entity, bool on, CancellationToken ct = default)
    {
        var path = on ? "turn_on" : "turn_off";
        var url = $"http://{_host}/switch/{entity}/{path}";
        try
        {
            using var resp = await _http.PostAsync(url, null, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"http://{_host}/", ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
