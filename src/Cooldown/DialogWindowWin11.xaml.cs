using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Cooldown.Themes;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class DialogWindowWin11 : Window, IFlashable
{
    public DialogWindowWin11()
    {
        InitializeComponent();
        Win11Palette.Apply(this);
        SourceInitialized += (_, _) =>
        {
            Win11Chrome.Enable(this);
            Win11Palette.Attach(this);
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
        SizeToContent = SizeToContent.Height;
        MaxHeight = 560;
        Width = vm.ModalProgress ? 280
            : vm.ModalJourney ? 420
            : vm.ModalTakeOffWarn || vm.ModalPutOnWarn ? 340
            : 560;
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
