using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChairOSC;

public class AppConfig
{
    public string EspHost { get; set; } = "192.168.81.151";
    public int OscPort { get; set; } = 9001;
    public string OscBind { get; set; } = "127.0.0.1";
    public bool Enabled { get; set; } = true;

    // Velocity calculation
    public double BaseIntensity { get; set; } = 0.20;     // floor when zone is touched (proximity>threshold) but not moving
    public double VelocityScale { get; set; } = 2.5;      // multiplier on |dProximity/dt|
    public double TouchThreshold { get; set; } = 0.05;    // below this, proximity is treated as "not touching"
    public int VelocityWindowMs { get; set; } = 150;      // rolling window for velocity calc
    public double MaxIntensity { get; set; } = 1.0;       // hard ceiling
    public int EspMinUpdateIntervalMs { get; set; } = 50; // throttle per-zone HTTP writes

    // Per-OSC-zone multiplier (applied after velocity calc, before clamp)
    public double MultBack { get; set; } = 1.0;
    public double MultLumbar { get; set; } = 1.0;
    public double MultLThigh { get; set; } = 1.0;
    public double MultRThigh { get; set; } = 1.0;
    public double MultLLeg { get; set; } = 1.0;
    public double MultRLeg { get; set; } = 1.0;

    // Hardware mapping: which hardware-zone (1..4) each OSC zone drives.
    // PLACEHOLDER — chair's physical zone-to-motor mapping not yet measured.
    // Update once empirically determined.
    public int HwZoneBack { get; set; } = 1;
    public int HwZoneLumbar { get; set; } = 2;
    public int HwZoneLThigh { get; set; } = 3;
    public int HwZoneRThigh { get; set; } = 3;  // shares motor with lthigh
    public int HwZoneLLeg { get; set; } = 4;
    public int HwZoneRLeg { get; set; } = 4;    // shares motor with lleg

    // ESP entity IDs for HTTP POSTs
    public string HeatEntity { get; set; } = "recliner3_massage_heat";
    public string IntensityEntityFormat { get; set; } = "recliner3_massage_zone_{0}_intensity";  // {0} = hw zone 1..4

    [JsonIgnore]
    public static string ConfigPath { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ChairOSC", "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null) return cfg;
            }
        }
        catch { /* fall through to defaults */ }
        return new AppConfig();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, opts));
    }
}
