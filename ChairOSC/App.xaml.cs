using System.Drawing;
using System.Windows;
using WinForms = System.Windows.Forms;
using Application = System.Windows.Application;
using StartupEventArgs = System.Windows.StartupEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace ChairOSC;

public partial class App : Application
{
    public static AppConfig Cfg { get; private set; } = AppConfig.Load();
    public static EspClient Esp { get; private set; } = null!;
    public static ZoneController Zc { get; private set; } = null!;
    public static OscListener Osc { get; private set; } = null!;

    private WinForms.NotifyIcon? _tray;
    private MainWindow? _window;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        Esp = new EspClient(Cfg.EspHost);
        Zc = new ZoneController(Cfg, Esp);
        Osc = new OscListener(Zc);

        Osc.Start(Cfg.OscBind, Cfg.OscPort);

        _tray = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "ChairOSC",
            Visible = true,
        };
        var menu = new WinForms.ContextMenuStrip();
        var openItem = menu.Items.Add("Open Settings");
        openItem.Click += (s, a) => ShowWindow();
        var enabledItem = (WinForms.ToolStripMenuItem)menu.Items.Add("Enabled");
        enabledItem.Checked = Cfg.Enabled;
        enabledItem.CheckOnClick = true;
        enabledItem.CheckedChanged += (s, a) =>
        {
            Cfg.Enabled = enabledItem.Checked;
            Cfg.Save();
        };
        menu.Items.Add("-");
        var exitItem = menu.Items.Add("Exit");
        exitItem.Click += (s, a) => Shutdown();
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (s, a) => ShowWindow();
    }

    private void ShowWindow()
    {
        if (_window == null || !_window.IsLoaded)
        {
            _window = new MainWindow();
            _window.Closed += (s, a) => _window = null;
        }
        _window.Show();
        _window.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Osc?.Stop(); } catch { }
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        Cfg.Save();
        base.OnExit(e);
    }
}
