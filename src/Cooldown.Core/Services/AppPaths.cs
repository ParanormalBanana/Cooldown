namespace Cooldown;

internal static class AppPaths
{
    public static string Root { get; private set; } = "";
    public static string DataFile { get; private set; } = "";
    public static string LogFile { get; private set; } = "";
    public static string WatchLock { get; private set; } = "";
    public static string CoversDir { get; private set; } = "";

    static AppPaths() => Ensure();

    public static void Ensure()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Root = Path.Combine(appData, "Cooldown");
        Directory.CreateDirectory(Root);
        DataFile = Path.Combine(Root, "state.json");
        LogFile = Path.Combine(Root, "cooldown.log");
        WatchLock = Path.Combine(Root, "watch.lock");
        CoversDir = Path.Combine(Root, "covers");
        Directory.CreateDirectory(CoversDir);
    }

    public static string ExePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            return path;
        return Path.Combine(AppContext.BaseDirectory, "Cooldown.exe");
    }

    public static string AgentPath()
    {
        var dir = Path.GetDirectoryName(ExePath()) ?? AppContext.BaseDirectory;
        var agent = Path.Combine(dir, "Cooldown.Agent.exe");
        return File.Exists(agent) ? agent : ExePath();
    }

    public static string AgentCommand(params string[] args)
    {
        var exe = AgentPath();
        var rest = string.Join(" ", args);
        return string.IsNullOrEmpty(rest) ? $"\"{exe}\"" : $"\"{exe}\" {rest}";
    }
}
