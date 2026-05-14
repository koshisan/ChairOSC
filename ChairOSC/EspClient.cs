using System.Net.Http;

namespace ChairOSC;

/// <summary>
/// Thin HTTP client over ESPHome's web_server REST API.
/// Endpoints used:
///   POST http://{host}/number/{entity}/set?value={float}
///   POST http://{host}/switch/{entity}/turn_on
///   POST http://{host}/switch/{entity}/turn_off
///
/// The local ESPHome web_server uses object_id (slugified entity name)
/// WITHOUT the HA device prefix — so it's "massage_zone_1_intensity",
/// not "recliner3_massage_zone_1_intensity".
///
/// All POSTs include an empty StringContent body — ESPHome's HTTPd rejects
/// requests with no Content-Length (returns HTTP 411).
/// </summary>
public class EspClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private string _host;
    private readonly AppConfig _cfg;

    private static readonly StringContent EmptyBody = new("");

    public EspClient(string host, AppConfig cfg) { _host = host; _cfg = cfg; }

    public void UpdateHost(string host) => _host = host;

    public async Task<bool> SetIntensityAsync(int hwZone, double value, CancellationToken ct = default)
    {
        var entity = string.Format(_cfg.IntensityEntityFormat, hwZone);
        var url = $"http://{_host}/number/{entity}/set?value={value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}";
        try
        {
            using var resp = await _http.PostAsync(url, new StringContent(""), ct).ConfigureAwait(false);
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
            using var resp = await _http.PostAsync(url, new StringContent(""), ct).ConfigureAwait(false);
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
