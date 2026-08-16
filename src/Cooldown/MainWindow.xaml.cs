using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class MainWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseLeave = 0x02A2;
    private const int HtMaxButton = 9;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpDoNotRound = 1;
    private const int BorderColor = 0x00C0C0C0;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            EnableCustomChrome();
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        };
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.SetViewportWidth(GameList.ActualWidth > 0 ? GameList.ActualWidth : ActualWidth - 48);
        };
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SetViewportWidth(GameList.ActualWidth > 0 ? GameList.ActualWidth : ActualWidth - 48);
    }

    private void Window_StateChanged(object sender, EventArgs e) => ApplyMaximizedLayout();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainViewModel vm)
            vm.CloseModal();
    }

    private void Stats_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.OpenProgressCommand.Execute(null);
    }

    private int _captionFlash;

    private async void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var token = ++_captionFlash;
        var active = (Brush)FindResource("ActiveCaptionFill");
        var inactive = (Brush)FindResource("InactiveCaptionFill");
        for (var i = 0; i < 3; i++)
        {
            if (token != _captionFlash) return;
            ModalCaption.Background = inactive;
            await Task.Delay(80);
            if (token != _captionFlash) return;
            ModalCaption.Background = active;
            await Task.Delay(80);
        }
    }

    private void ModalCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ApplyMaximizedLayout()
    {
        Root.Margin = WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(0);
        MaxIcon.Visibility = WindowState == WindowState.Maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = WindowState == WindowState.Maximized ? Visibility.Visible : Visibility.Collapsed;
        MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    private void EnableCustomChrome()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
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

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmNcMouseLeave) return IntPtr.Zero;
        if (msg != WmNcHitTest) return IntPtr.Zero;
        if (OverMaxButton(lParam))
        {
            handled = true;
            return new IntPtr(HtMaxButton);
        }
        return IntPtr.Zero;
    }

    private bool OverMaxButton(IntPtr lParam)
    {
        var packed = lParam.ToInt64();
        var screen = new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null || MaxButton.ActualWidth <= 0) return false;
        var dip = source.CompositionTarget.TransformFromDevice.Transform(screen);
        var local = MaxButton.PointFromScreen(dip);
        return local.X >= 0 && local.Y >= 0 && local.X <= MaxButton.ActualWidth && local.Y <= MaxButton.ActualHeight;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
