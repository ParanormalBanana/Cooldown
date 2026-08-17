using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Cooldown.Themes;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class DialogWindowXp : Window, IFlashable
{
    private int _flash;

    public DialogWindowXp()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            XpChrome.Enable(this);
            XpChrome.ApplyTopRoundRegion(this);
        };
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
            or nameof(MainViewModel.ModalJourney)
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
        Width = vm.ModalJourney ? 420
            : vm.ModalTakeOffWarn || vm.ModalPutOnWarn ? 320
            : 560;
        Dispatcher.BeginInvoke(CenterOnOwner, DispatcherPriority.Loaded);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyDialogSize();
        CenterOnOwner();
        XpChrome.ApplyTopRoundRegion(this);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) =>
        XpChrome.ApplyTopRoundRegion(this);

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
