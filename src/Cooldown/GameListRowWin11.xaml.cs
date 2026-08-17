using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cooldown.Themes;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class GameListRowWin11 : UserControl
{
    private bool _pressed;

    public GameListRowWin11()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            _pressed = false;
            RefreshChrome();
            RequestCover();
        };
        Loaded += (_, _) => RequestCover();
    }

    private void RequestCover()
    {
        if (DataContext is GameItem item && Window.GetWindow(this)?.DataContext is MainViewModel vm)
            vm.OnListRowRealized(item);
    }

    private MainViewModel? Vm => Window.GetWindow(this)?.DataContext as MainViewModel;

    private void Row_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Root.CaptureMouse();
        _pressed = true;
        RefreshChrome();
        e.Handled = true;
    }

    private void Row_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!Root.IsMouseCaptured) return;
        var inside = Root.IsMouseOver;
        Root.ReleaseMouseCapture();
        _pressed = false;
        RefreshChrome();
        if (inside && DataContext is GameItem item)
            Vm?.SelectGameCommand.Execute(item);
        e.Handled = true;
    }

    private void Row_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _pressed = false;
        RefreshChrome();
    }

    private void Row_MouseEnter(object sender, MouseEventArgs e) => RefreshChrome();

    private void Row_MouseLeave(object sender, MouseEventArgs e) => RefreshChrome();

    private void RefreshChrome()
    {
        if (_pressed)
        {
            Root.Background = Fill("SubtleFillStrong");
            Root.Opacity = 0.92;
            return;
        }
        Root.Opacity = 1;
        Root.Background = IsMouseOver ? Fill("SubtleFill") : Brushes.Transparent;
    }

    private Brush Fill(string key) =>
        TryFindResource(key) as Brush ?? Brushes.Transparent;

    private void CardMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            Win11Palette.ApplyTo(menu.Resources);
            if (Vm is { } vm)
                GameListRow.SyncViewMenu(menu, vm);
        }
    }

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is GameItem item) Vm?.HideGame(item);
    }

    private void Unhide_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is GameItem item) Vm?.UnhideGame(item);
    }

    private void NotAGame_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is GameItem item) Vm?.MarkNotAGame(item);
    }

    private void ToggleHidden_Click(object sender, RoutedEventArgs e) => Vm?.ToggleHidden();

    private void ToggleCooldownsOnTop_Click(object sender, RoutedEventArgs e) => Vm?.ToggleCooldownsOnTop();

    private void ShowGrid_Click(object sender, RoutedEventArgs e) => Vm?.ShowGridView();

    private void ShowDetails_Click(object sender, RoutedEventArgs e) => Vm?.ShowDetailsView();

    private void Settings_Click(object sender, RoutedEventArgs e) => Vm?.OpenSettings();
}
