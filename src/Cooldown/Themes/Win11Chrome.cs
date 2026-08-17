using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Cooldown.Themes;

internal static class Win11Palette
{
    private const int WmSettingChange = 0x001A;
    private const int WmDwmColorizationColorChanged = 0x0320;
    private static readonly List<WeakReference<Window>> Windows = [];
    private static bool _pending;

    public static bool IsLight { get; private set; } = true;

    public static void Attach(Window window)
    {
        Apply(window);
        Windows.Add(new WeakReference<Window>(window));
        window.Closed += (_, _) => Detach(window);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
    }

    public static void Apply(Window window) => ApplyTo(window.Resources);

    public static void ApplyTo(ResourceDictionary resources)
    {
        IsLight = ReadLight();
        var uri = new Uri(
            IsLight
                ? "pack://application:,,,/Themes/Win11Light.xaml"
                : "pack://application:,,,/Themes/Win11Dark.xaml",
            UriKind.Absolute);
        var colors = new ResourceDictionary { Source = uri };
        var merged = resources.MergedDictionaries;
        var replaced = false;
        for (var i = 0; i < merged.Count; i++)
        {
            var src = merged[i].Source?.OriginalString ?? "";
            if (src.Contains("Win11Light", StringComparison.OrdinalIgnoreCase)
                || src.Contains("Win11Dark", StringComparison.OrdinalIgnoreCase))
            {
                merged[i] = colors;
                replaced = true;
                break;
            }
        }
        if (!replaced)
            merged.Insert(0, colors);
        OverlayAccent(resources);
    }

    private static void OverlayAccent(ResourceDictionary resources)
    {
        var accent = ReadAccent();
        var text = ContrastingText(accent);
        resources["Accent"] = Brush(accent);
        resources["HotTrack"] = Brush(accent);
        resources["AccentText"] = Brush(text);
        resources["AccentSubtle"] = Brush(Color.FromArgb(0x26, accent.R, accent.G, accent.B));
    }

    private static void Detach(Window window)
    {
        for (var i = Windows.Count - 1; i >= 0; i--)
        {
            if (!Windows[i].TryGetTarget(out var live) || live == window)
                Windows.RemoveAt(i);
        }
    }

    private static void ApplyAll()
    {
        IsLight = ReadLight();
        for (var i = Windows.Count - 1; i >= 0; i--)
        {
            if (!Windows[i].TryGetTarget(out var window))
            {
                Windows.RemoveAt(i);
                continue;
            }
            Apply(window);
            Win11Chrome.Enable(window);
        }
    }

    private static bool ReadLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
                return value != 0;
        }
        catch
        {
            // older Windows
        }
        return true;
    }

    private static Color ReadAccent()
    {
        if (TryReadPackedColor(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentColorMenu", out var menu))
            return menu;
        if (TryReadPackedColor(@"Software\Microsoft\Windows\DWM", "AccentColor", out var dwm))
            return dwm;
        return Color.FromRgb(0x00, 0x5F, 0xB8);
    }

    private static bool TryReadPackedColor(string path, string name, out Color color)
    {
        color = default;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            var packed = key?.GetValue(name) switch
            {
                int i => unchecked((uint)i),
                uint u => u,
                _ => 0u
            };
            if (packed == 0) return false;
            var r = (byte)(packed & 0xFF);
            var g = (byte)((packed >> 8) & 0xFF);
            var b = (byte)((packed >> 16) & 0xFF);
            var a = (byte)((packed >> 24) & 0xFF);
            if (a == 0) a = 255;
            color = Color.FromArgb(a, r, g, b);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Color ContrastingText(Color accent)
    {
        var luminance = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255.0;
        return luminance > 0.55 ? Colors.Black : Colors.White;
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var accentChanged = msg == WmDwmColorizationColorChanged;
        var immersive = msg == WmSettingChange && lParam != IntPtr.Zero
            && string.Equals(Marshal.PtrToStringUni(lParam), "ImmersiveColorSet", StringComparison.Ordinal);
        if ((accentChanged || immersive) && !_pending)
        {
            _pending = true;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                _pending = false;
                ApplyAll();
            });
        }
        return IntPtr.Zero;
    }
}

internal static class Win11Chrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static void Enable(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var dark = Win11Palette.IsLight ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch
        {
            // older Windows
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
