using System.Windows;
using Cooldown.ViewModels;

namespace Cooldown;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppPaths.Ensure();
        Log.Configure();
        base.OnStartup(e);
        try
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm };
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
}
