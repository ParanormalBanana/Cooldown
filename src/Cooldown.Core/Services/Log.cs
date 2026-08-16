namespace Cooldown;

internal static class Log
{
    private static readonly object Gate = new();

    public static void Configure()
    {
        // AppPaths.Ensure() must have run. Kept for call-site compatibility.
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        Write("ERROR", ex is null ? message : $"{message}: {ex}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            var path = AppPaths.LogFile;
            if (string.IsNullOrEmpty(path)) return;
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}";
            lock (Gate)
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 512_000)
                {
                    var bak = path + ".1";
                    File.Delete(bak);
                    File.Move(path, bak);
                }
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // logging must never take the app down
        }
    }
}
