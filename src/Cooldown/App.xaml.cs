using System.Windows;
using System.Windows.Controls.Primitives;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.Ensure();
        Log.Configure();
        Resources[SystemParameters.MenuPopupAnimationKey] = PopupAnimation.None;
        base.OnStartup(e);
        try
        {
            var vm = new MainViewModel();
            var window = CreateShell(vm);
            window.Show();
            vm.Start(Dispatcher);
        }
        catch (Exception ex)
        {
            Log.Error("UI startup failed", ex);
            MessageBox.Show(ex.Message, "Cooldown", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    public static void RecreateShell()
    {
        if (Current is not App app) return;
        app.RecreateShellCore();
    }

    private void RecreateShellCore()
    {
        if (MainWindow is not Window old || old.DataContext is not MainViewModel vm)
            return;
        vm.CloseModal();
        var next = CreateShell(vm);
        next.Left = old.Left;
        next.Top = old.Top;
        next.Width = old.Width;
        next.Height = old.Height;
        var state = old.WindowState;
        MainWindow = next;
        next.Show();
        next.WindowState = state;
        old.Close();
    }

    private static Window CreateShell(MainViewModel vm)
    {
        Window window = Theme.Normalize(vm.SelectedTheme) switch
        {
            Theme.Xp2001 => new MainWindowXp(),
            Theme.Win112021 => new MainWindowWin11(),
            _ => new MainWindow(),
        };
        window.DataContext = vm;
        return window;
    }
}
