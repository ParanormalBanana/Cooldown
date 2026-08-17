using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Cooldown.Themes;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class MainWindowXp : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int WmNcMouseLeave = 0x02A2;
    private const int HtMaxButton = 9;

    private Window? _dialog;
    private bool _syncingDialog;

    public MainWindowXp()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            XpChrome.Enable(this);
            XpChrome.ApplyTopRoundRegion(this);
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        };
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.SetViewportWidth(GameList.ActualWidth > 0 ? GameList.ActualWidth : ActualWidth - 48);
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.PropertyChanged += OnViewModelPropertyChanged;
        };
        Activated += (_, _) =>
        {
            if (_dialog is { IsVisible: true } && !_dialog.IsActive)
            {
                _dialog.Activate();
                if (_dialog is IFlashable flash)
                    flash.Flash();
            }
        };
        Closed += (_, _) =>
        {
            if (DataContext is INotifyPropertyChanged vm)
                vm.PropertyChanged -= OnViewModelPropertyChanged;
            _dialog?.Close();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ModalOpen) or null)
            SyncDialog();
    }

    private void SyncDialog()
    {
        if (_syncingDialog) return;
        var open = DataContext is MainViewModel { ModalOpen: true };
        if (open)
            ShowDialogWindow();
        else
            CloseDialogWindow();
    }

    private void ShowDialogWindow()
    {
        if (_dialog != null) return;
        Window window = DataContext is MainViewModel { ModalSettings: true }
            ? new SettingsWindowXp()
            : new DialogWindowXp();
        window.Owner = this;
        window.DataContext = DataContext;
        window.Closed += (_, _) =>
        {
            _dialog = null;
            if (DataContext is MainViewModel { ModalOpen: true } vm)
            {
                _syncingDialog = true;
                try { vm.CloseModal(); }
                finally { _syncingDialog = false; }
            }
        };
        _dialog = window;
        window.Show();
    }

    private void CloseDialogWindow()
    {
        if (_dialog == null) return;
        var dialog = _dialog;
        _dialog = null;
        dialog.Close();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        XpChrome.ApplyTopRoundRegion(this);
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

    private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_dialog is IFlashable flash)
            flash.Flash();
        else
            _dialog?.Activate();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ApplyMaximizedLayout()
    {
        var max = WindowState == WindowState.Maximized;
        Root.Margin = max ? new Thickness(8) : new Thickness(0);
        Root.CornerRadius = max ? new CornerRadius(0) : new CornerRadius(XpChrome.CornerDip, XpChrome.CornerDip, 0, 0);
        CaptionBar.CornerRadius = Root.CornerRadius;
        MaxIcon.Visibility = max ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = max ? Visibility.Visible : Visibility.Collapsed;
        MaxButton.ToolTip = max ? "Restore" : "Maximize";
        XpChrome.ApplyTopRoundRegion(this);
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
}
