using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class GameListRow : UserControl
{
    private static readonly SolidColorBrush Black = Brush("#000000");
    private static readonly SolidColorBrush White = Brush("#FFFFFF");
    private static readonly SolidColorBrush Shadow = Brush("#808080");
    private static readonly SolidColorBrush Light = Brush("#DFDFDF");
    private static readonly SolidColorBrush Face = Brush("#C0C0C0");
    private static readonly SolidColorBrush HoverFace = Brush("#DFDFDF");

    private bool _pressed;

    public GameListRow()
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
            Root.Background = Face;
            Root.BorderBrush = White;
            BevelWhite.BorderBrush = Black;
            BevelDark.BorderBrush = Light;
            BevelLight.BorderBrush = Shadow;
            PressShift.Margin = new Thickness(1, 1, -1, -1);
            return;
        }
        Root.Background = IsMouseOver ? HoverFace : Face;
        Root.BorderBrush = Black;
        BevelWhite.BorderBrush = White;
        BevelDark.BorderBrush = Shadow;
        BevelLight.BorderBrush = Light;
        PressShift.Margin = new Thickness(0);
    }

    private void CardMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu && Vm is { } vm)
            SyncViewMenu(menu, vm);
    }

    internal static void SyncViewMenu(ContextMenu menu, MainViewModel vm)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Name == "ToggleHiddenItem")
                item.IsChecked = vm.ShowHidden;
            else if (item.Name == "ToggleCooldownsOnTopItem")
                item.IsChecked = vm.CooldownsOnTop;
            else if (item.Name == "GridViewItem")
                item.IsChecked = vm.GridView;
            else if (item.Name == "ListViewItem")
                item.IsChecked = vm.DetailsView;
        }
    }

    internal static void SyncScrollMode(ListBox list, MainViewModel vm)
    {
        GridScrollTimer.Stop();
        _gridScroll = null;
        VirtualizingPanel.SetScrollUnit(list, vm.DetailsView ? ScrollUnit.Item : ScrollUnit.Pixel);
    }

    private const double GridPixelsPerNotch = 16;
    private const double GridScrollEase = 0.22;
    private static readonly DispatcherTimer GridScrollTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(16)
    };
    private static ScrollViewer? _gridScroll;
    private static double _gridTarget;

    static GameListRow() => GridScrollTimer.Tick += (_, _) => StepGridScroll();

    internal static void ScrollOneLine(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ListBox list || e.Delta == 0) return;
        if (FindScrollViewer(list) is not { } sv) return;
        e.Handled = true;
        if (Window.GetWindow(list)?.DataContext is MainViewModel { DetailsView: true })
        {
            GridScrollTimer.Stop();
            if (e.Delta > 0) sv.LineUp();
            else sv.LineDown();
            return;
        }

        if (!ReferenceEquals(_gridScroll, sv))
        {
            _gridScroll = sv;
            _gridTarget = sv.VerticalOffset;
        }
        _gridTarget = Math.Clamp(
            _gridTarget - e.Delta * (GridPixelsPerNotch / 120.0),
            0,
            sv.ScrollableHeight);
        if (!GridScrollTimer.IsEnabled)
            GridScrollTimer.Start();
    }

    private static void StepGridScroll()
    {
        if (_gridScroll is not { } sv)
        {
            GridScrollTimer.Stop();
            return;
        }

        _gridTarget = Math.Clamp(_gridTarget, 0, sv.ScrollableHeight);
        var remaining = _gridTarget - sv.VerticalOffset;
        if (Math.Abs(remaining) < 0.4)
        {
            sv.ScrollToVerticalOffset(_gridTarget);
            GridScrollTimer.Stop();
            return;
        }

        sv.ScrollToVerticalOffset(sv.VerticalOffset + remaining * GridScrollEase);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var hit = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (hit is not null) return hit;
        }
        return null;
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

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
