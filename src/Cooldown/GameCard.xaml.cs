using System.Windows;
using System.Windows.Controls;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class GameCard : UserControl
{
    public GameCard()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RequestCover();
        Loaded += (_, _) => RequestCover();
    }

    private void RequestCover()
    {
        if (DataContext is GameItem item && Window.GetWindow(this)?.DataContext is MainViewModel vm)
            vm.OnCardRealized(item);
    }

    private MainViewModel? Vm => Window.GetWindow(this)?.DataContext as MainViewModel;

    private void CardMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && Vm is { } vm)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (item.Name == "ToggleHiddenItem")
                    item.Header = vm.ShowHiddenLabel;
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
}
