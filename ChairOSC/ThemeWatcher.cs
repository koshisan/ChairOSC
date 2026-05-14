using Microsoft.Win32;

namespace ChairOSC;

/// <summary>
/// Detects the Windows tray/taskbar theme (light vs dark) via the
/// SystemUsesLightTheme registry value and emits a change event so the
/// caller can swap NotifyIcon.Icon when the user toggles theme.
/// </summary>
public static class ThemeWatcher
{
    public enum Theme { Light, Dark }

    public static event Action<Theme>? Changed;

    private static Theme _current = Read();

    public static Theme Current => _current;

    public static void Start()
    {
        SystemEvents.UserPreferenceChanged += OnUserPrefChanged;
    }

    public static void Stop()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPrefChanged;
    }

    private static void OnUserPrefChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        var fresh = Read();
        if (fresh == _current) return;
        _current = fresh;
        Changed?.Invoke(fresh);
    }

    private static Theme Read()
    {
        try
        {
            // SystemUsesLightTheme controls taskbar/tray/start. 1 = light, 0 = dark.
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("SystemUsesLightTheme");
            if (v is int i) return i == 1 ? Theme.Light : Theme.Dark;
        }
        catch { /* registry unavailable → assume light */ }
        return Theme.Light;
    }
}
