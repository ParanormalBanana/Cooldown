using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Cooldown.Themes;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class SettingsWindowWin11 : Window, IFlashable
{
    public SettingsWindowWin11()
    {
        InitializeComponent();
        Win11Palette.Apply(this);
        SourceInitialized += (_, _) =>
        {
            Win11Chrome.Enable(this);
            Win11Palette.Attach(this);
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainViewModel vm)
        {
            vm.CloseModal();
            e.Handled = true;
        }
    }

    public void Flash()
    {
        Activate();
        try
        {
            FlashWindow(new WindowInteropHelper(this).Handle, true);
        }
        catch
        {
            // older Windows
        }
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool invert);
}
