using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Cooldown.ViewModels;

namespace Cooldown;

internal interface IFlashable
{
    void Flash();
}

public partial class DialogWindow : Window, IFlashable
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpDoNotRound = 1;
    private const int BorderColor = 0x00C0C0C0;

    private int _flash;

    public DialogWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => EnableCustomChrome();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainViewModel vm)
        {
            vm.CloseModal();
            e.Handled = true;
        }
    }

    private void Caption_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) != null)
            return;
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    public async void Flash()
    {
        Activate();
        var token = ++_flash;
        var active = (Brush)FindResource("ActiveCaptionFill");
        var inactive = (Brush)FindResource("InactiveCaptionFill");
        try
        {
            FlashWindow(new WindowInteropHelper(this).Handle, true);
        }
        catch
        {
            // older Windows
        }
        for (var i = 0; i < 3; i++)
        {
            if (token != _flash) return;
            ModalCaption.Background = inactive;
            await Task.Delay(80);
            if (token != _flash) return;
            ModalCaption.Background = active;
            await Task.Delay(80);
        }
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

    private static T? FindAncestor<T>(DependencyObject? start) where T : class
    {
        while (start != null)
        {
            if (start is T match) return match;
            start = VisualTreeHelper.GetParent(start);
        }
        return null;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool invert);
}
