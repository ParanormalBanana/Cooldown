namespace Cooldown;

internal static class Program
{
    private const string MutexName = @"Local\Cooldown.Agent";

    [STAThread]
    private static void Main(string[] args)
    {
        AppPaths.Ensure();
        Log.Configure();

        // One-shots must not take the tray mutex (the tray holds it for life).
        if (Has(args, "--wipe"))
        {
            Uninstaller.TryElevatedWipeRequest();
            return;
        }
        if (Has(args, "--now"))
        {
            Worker.Run(["startup", "schedule"]);
            return;
        }

        using var mutex = new Mutex(true, MutexName, out var created);
        if (!created) return;

        var startup = Has(args, "--startup");
        var tray = startup || Has(args, "--tray");
        var events = startup ? new[] { "startup", "schedule" } : new[] { "schedule" };

        if (!tray)
        {
            Worker.Run(events);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApp(events));
    }

    private static bool Has(string[] args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
}
