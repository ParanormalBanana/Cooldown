using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) =>
        {
            if (DataContext is INotifyPropertyChanged vm)
                vm.PropertyChanged -= OnViewModelPropertyChanged;
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
        ApplyDialogSize();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ModalTakeOffWarn)
            or nameof(MainViewModel.ModalPutOnWarn)
            or nameof(MainViewModel.ModalTakeOff)
            or nameof(MainViewModel.ModalPutOn)
            or nameof(MainViewModel.ModalProgress)
            or null)
            ApplyDialogSize();
    }

    private void ApplyDialogSize()
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.ModalProgress)
        {
            SizeToContent = SizeToContent.Manual;
            Width = 260;
            Height = 260;
            MaxHeight = 260;
            CenterOnOwner();
            return;
        }

        SizeToContent = SizeToContent.Height;
        MaxHeight = 560;
        Width = vm.ModalTakeOffWarn || vm.ModalPutOnWarn ? 320 : 560;
        Dispatcher.BeginInvoke(CenterOnOwner, DispatcherPriority.Loaded);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyDialogSize();
        CenterOnOwner();
    }

    private void CenterOnOwner()
    {
        if (Owner is not { } owner) return;
        Left = owner.Left + (owner.ActualWidth - ActualWidth) / 2;
        Top = owner.Top + (owner.ActualHeight - ActualHeight) / 2;
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
