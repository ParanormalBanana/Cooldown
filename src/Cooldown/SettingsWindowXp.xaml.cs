using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Cooldown.Themes;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class SettingsWindowXp : Window, IFlashable
{
    private int _flash;

    public SettingsWindowXp()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            XpChrome.Enable(this);
            XpChrome.ApplyTopRoundRegion(this);
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        XpChrome.ApplyTopRoundRegion(this);

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) =>
        XpChrome.ApplyTopRoundRegion(this);

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

    private static T? FindAncestor<T>(DependencyObject? start) where T : class
    {
        while (start != null)
        {
            if (start is T match) return match;
            start = VisualTreeHelper.GetParent(start);
        }
        return null;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool invert);
}
