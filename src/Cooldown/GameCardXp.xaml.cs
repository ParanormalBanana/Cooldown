using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class GameCardXp : UserControl
{
    public GameCardXp()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            SetPressed(false);
            RequestCover();
        };
        Loaded += (_, _) => RequestCover();
    }

    private void RequestCover()
    {
        if (DataContext is GameItem item && Window.GetWindow(this)?.DataContext is MainViewModel vm)
            vm.OnCardRealized(item);
    }

    private MainViewModel? Vm => Window.GetWindow(this)?.DataContext as MainViewModel;

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Root.CaptureMouse();
        SetPressed(true);
        e.Handled = true;
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!Root.IsMouseCaptured) return;
        var inside = Root.IsMouseOver;
        Root.ReleaseMouseCapture();
        SetPressed(false);
        if (inside && DataContext is GameItem item)
            Vm?.SelectGameCommand.Execute(item);
        e.Handled = true;
    }

    private void Card_LostMouseCapture(object sender, MouseEventArgs e) => SetPressed(false);

    private void SetPressed(bool pressed) =>
        PressShift.Margin = pressed ? new Thickness(1, 1, -1, -1) : new Thickness(0);

    private void CardMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && Vm is { } vm)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (item.Name == "ToggleHiddenItem")
                    item.IsChecked = vm.ShowHidden;
                else if (item.Name == "ToggleCooldownsOnTopItem")
                    item.IsChecked = vm.CooldownsOnTop;
            }
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

    private void Settings_Click(object sender, RoutedEventArgs e) => Vm?.OpenSettings();
}
