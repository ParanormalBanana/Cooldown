using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Cooldown.Themes;

internal static class XpChrome
{
    public const int CornerDip = 8;
    public const int BorderColor = unchecked((int)0xFFFFFFFE);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpDoNotRound = 1;
    private const int RgnOr = 2;

    public static void Enable(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var dark = 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            var round = DwmwcpDoNotRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
            var border = BorderColor;
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
        }
        catch
        {
            // older Windows
        }
    }

    public static void ApplyTopRoundRegion(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || window.ActualWidth <= 0 || window.ActualHeight <= 0) return;
            if (window.WindowState == WindowState.Maximized)
            {
                SetWindowRgn(hwnd, IntPtr.Zero, true);
                return;
            }

            var source = PresentationSource.FromVisual(window);
            var m = source?.CompositionTarget?.TransformToDevice;
            var scaleX = m?.M11 ?? 1;
            var scaleY = m?.M22 ?? 1;
            var w = Math.Max(1, (int)Math.Round(window.ActualWidth * scaleX));
            var h = Math.Max(1, (int)Math.Round(window.ActualHeight * scaleY));
            var radius = Math.Max(1, (int)Math.Round(CornerDip * scaleX));
            var diameter = radius * 2;

            var rounded = CreateRoundRectRgn(0, 0, w + 1, h + 1, diameter, diameter);
            var square = CreateRectRgn(0, radius, w + 1, h + 1);
            CombineRgn(rounded, rounded, square, RgnOr);
            DeleteObject(square);
            SetWindowRgn(hwnd, rounded, true);
        }
        catch
        {
            // older Windows
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int combineMode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr hrgn, bool redraw);
}
