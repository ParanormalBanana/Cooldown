namespace Cooldown;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AppPaths.Ensure();
        Log.Configure();
        var startup = args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        Worker.Run(startup ? ["startup", "schedule"] : ["schedule"]);
    }
}
