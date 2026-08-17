using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Cooldown.Themes;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class MainWindowWin11 : Window
{
    private Window? _dialog;
    private bool _syncingDialog;

    public MainWindowWin11()
    {
        InitializeComponent();
        Win11Palette.Apply(this);
        SourceInitialized += (_, _) =>
        {
            Win11Chrome.Enable(this);
            Win11Palette.Attach(this);
        };
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SetViewportWidth(GameList.ActualWidth > 0 ? GameList.ActualWidth : ActualWidth - 48);
                GameListRow.SyncScrollMode(GameList, vm);
            }
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
        if (e.PropertyName is nameof(MainViewModel.DetailsView) or null
            && DataContext is MainViewModel vm)
            GameListRow.SyncScrollMode(GameList, vm);
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
            ? new SettingsWindowWin11()
            : new DialogWindowWin11();
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
        if (DataContext is MainViewModel vm)
            vm.SetViewportWidth(GameList.ActualWidth > 0 ? GameList.ActualWidth : ActualWidth - 48);
    }

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

    private void GameList_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        GameListRow.ScrollOneLine(sender, e);

    private void Scrim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_dialog is IFlashable flash)
            flash.Flash();
        else
            _dialog?.Activate();
    }

    private void ThemeMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
            Win11Palette.ApplyTo(menu.Resources);
    }
}
