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
        Esp = new EspClient(Cfg.EspHost, Cfg);
        Zc = new ZoneController(Cfg, Esp);
        Osc = new OscListener(Zc);

        Osc.Start(Cfg.OscBind, Cfg.OscPort);

        // Load the icon variant that matches the current Windows tray theme.
        // chair_light.ico has dark strokes (visible on light tray);
        // chair_dark.ico has white strokes (visible on dark tray).
        _tray = new WinForms.NotifyIcon
        {
            Icon = LoadIconForTheme(ThemeWatcher.Current),
            Text = "ChairOSC",
            Visible = true,
        };
        ThemeWatcher.Start();
        ThemeWatcher.Changed += t =>
        {
            if (_tray != null) _tray.Icon = LoadIconForTheme(t);
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

        // Open the settings window on launch unless the user has opted into
        // start-to-tray. The tray icon is always present either way.
        if (!Cfg.StartToTray) ShowWindow();
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
        ThemeWatcher.Stop();
        try { Osc?.Stop(); } catch { }
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        Cfg.Save();
        base.OnExit(e);
    }

    private Icon LoadIconForTheme(ThemeWatcher.Theme theme)
    {
        var path = theme == ThemeWatcher.Theme.Dark ? "chair_dark.ico" : "chair_light.ico";
        try
        {
            var info = GetResourceStream(new Uri(path, UriKind.Relative));
            if (info != null)
            {
                using var stream = info.Stream;
                return new Icon(stream);
            }
        }
        catch { /* fall through */ }
        return SystemIcons.Application;
    }
}
