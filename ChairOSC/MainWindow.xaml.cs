using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace ChairOSC;

public partial class MainWindow : Window
{
    private const int MaxLogLines = 200;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly DispatcherTimer _pingTimer = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadFromConfig();
        WireEvents();

        App.Zc.Log += AddLog;
        App.Osc.Log += AddLog;
        App.Zc.IntensityChanged += (hw, val) => Dispatcher.BeginInvoke(() => SetZoneDisplay(hw, val));
        App.Zc.HeatChanged += on => Dispatcher.BeginInvoke(() =>
            HeatStatusText.Text = on ? "ON" : "off");
        App.Osc.RawOsc += (path, value) => Dispatcher.BeginInvoke(() =>
            AddLog($"OSC {path} = {value:0.000}"));

        _pingTimer.Interval = TimeSpan.FromSeconds(5);
        _pingTimer.Tick += async (s, a) => await UpdatePing();
        _pingTimer.Start();
        _ = UpdatePing();
    }

    private void LoadFromConfig()
    {
        var c = App.Cfg;
        EspHostBox.Text = c.EspHost;
        OscBindBox.Text = c.OscBind;
        OscPortBox.Text = c.OscPort.ToString(Inv);
        HeatEntityBox.Text = c.HeatEntity;
        EnabledBox.IsChecked = c.Enabled;
        StartToTrayBox.IsChecked = c.StartToTray;

        BaseIntensityBox.Text = c.BaseIntensity.ToString("0.00", Inv);
        VelocityScaleBox.Text = c.VelocityScale.ToString("0.00", Inv);
        TouchThresholdBox.Text = c.TouchThreshold.ToString("0.00", Inv);
        WindowMsBox.Text = c.VelocityWindowMs.ToString(Inv);
        MaxIntensityBox.Text = c.MaxIntensity.ToString("0.00", Inv);
        EspMinIntervalBox.Text = c.EspMinUpdateIntervalMs.ToString(Inv);
    }

    private void WireEvents()
    {
        ApplyBtn.Click += (s, a) => ApplySettings();
        PingBtn.Click += async (s, a) => await UpdatePing();
        ReconnectBtn.Click += (s, a) => RestartOsc();
        ClearLogBtn.Click += (s, a) => LogList.Items.Clear();

        Z1Slider.ValueChanged += (s, a) => Z1Value.Text = Z1Slider.Value.ToString("0.00", Inv);
        Z2Slider.ValueChanged += (s, a) => Z2Value.Text = Z2Slider.Value.ToString("0.00", Inv);
        Z3Slider.ValueChanged += (s, a) => Z3Value.Text = Z3Slider.Value.ToString("0.00", Inv);
        Z4Slider.ValueChanged += (s, a) => Z4Value.Text = Z4Slider.Value.ToString("0.00", Inv);

        Z1Send.Click += async (s, a) => await App.Esp.SetIntensityAsync(1, Z1Slider.Value);
        Z2Send.Click += async (s, a) => await App.Esp.SetIntensityAsync(2, Z2Slider.Value);
        Z3Send.Click += async (s, a) => await App.Esp.SetIntensityAsync(3, Z3Slider.Value);
        Z4Send.Click += async (s, a) => await App.Esp.SetIntensityAsync(4, Z4Slider.Value);

        HeatSend.Click += async (s, a) =>
            await App.Esp.SetSwitchAsync(App.Cfg.HeatEntity, HeatToggle.IsChecked ?? false);

        AllOffBtn.Click += async (s, a) =>
        {
            Z1Slider.Value = 0; Z2Slider.Value = 0; Z3Slider.Value = 0; Z4Slider.Value = 0;
            for (int i = 1; i <= 4; i++) await App.Esp.SetIntensityAsync(i, 0);
        };
    }

    private void ApplySettings()
    {
        var c = App.Cfg;
        c.EspHost = EspHostBox.Text.Trim();
        c.OscBind = OscBindBox.Text.Trim();
        if (int.TryParse(OscPortBox.Text, NumberStyles.Integer, Inv, out var port)) c.OscPort = port;
        c.HeatEntity = HeatEntityBox.Text.Trim();
        c.Enabled = EnabledBox.IsChecked ?? true;
        c.StartToTray = StartToTrayBox.IsChecked ?? false;

        if (double.TryParse(BaseIntensityBox.Text, NumberStyles.Float, Inv, out var b)) c.BaseIntensity = b;
        if (double.TryParse(VelocityScaleBox.Text, NumberStyles.Float, Inv, out var v)) c.VelocityScale = v;
        if (double.TryParse(TouchThresholdBox.Text, NumberStyles.Float, Inv, out var t)) c.TouchThreshold = t;
        if (int.TryParse(WindowMsBox.Text, NumberStyles.Integer, Inv, out var w)) c.VelocityWindowMs = w;
        if (double.TryParse(MaxIntensityBox.Text, NumberStyles.Float, Inv, out var m)) c.MaxIntensity = m;
        if (int.TryParse(EspMinIntervalBox.Text, NumberStyles.Integer, Inv, out var u)) c.EspMinUpdateIntervalMs = u;

        c.Save();
        App.Esp.UpdateHost(c.EspHost);
        App.Zc.RebuildCalculators();
        AddLog("Settings saved.");
    }

    private void RestartOsc()
    {
        App.Osc.Stop();
        App.Osc.Start(App.Cfg.OscBind, App.Cfg.OscPort);
    }

    private async Task UpdatePing()
    {
        var ok = await App.Esp.PingAsync();
        EspStatusText.Text = $"ESP {App.Cfg.EspHost}: {(ok ? "online" : "OFFLINE")}";
    }

    private void SetZoneDisplay(int hw, double val)
    {
        var v = Math.Clamp(val, 0.0, 1.0);
        switch (hw)
        {
            case 1: Zone1Bar.Value = v; Zone1Text.Text = v.ToString("0.00", Inv); break;
            case 2: Zone2Bar.Value = v; Zone2Text.Text = v.ToString("0.00", Inv); break;
            case 3: Zone3Bar.Value = v; Zone3Text.Text = v.ToString("0.00", Inv); break;
            case 4: Zone4Bar.Value = v; Zone4Text.Text = v.ToString("0.00", Inv); break;
        }
    }

    private void AddLog(string line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var ts = DateTime.Now.ToString("HH:mm:ss.fff");
            LogList.Items.Add($"{ts}  {line}");
            while (LogList.Items.Count > MaxLogLines) LogList.Items.RemoveAt(0);
            LogList.ScrollIntoView(LogList.Items[^1]);
        });
    }
}
