using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Cooldown.Models;

namespace Cooldown;

internal static class Uninstaller
{
    private static readonly string[] SafeMarkers =
    [
        @"steamapps\common", "steamapps/common", "epic games", "gog galaxy", "gog games",
        "xboxgames", "ubisoft", "origin games", "ea games", "battle.net", "blizzard",
        "rockstar games", "riot games",
    ];

    public static bool UninstallQuietly(Game game)
    {
        if (GamePayload.IsGameRunning(game.InstallPath))
        {
            Log.Info($"Aborting uninstall of {game.Name}, still running");
            return false;
        }
        var ok = UninstallHere(game);
        if (ok || IsElevated()) return ok;
        Log.Info($"Retrying {game.Name} elevated");
        return RetryElevated(game);
    }

    /// <summary>
    /// Elevated one-shot: wipe the game written to wipe.json, then delete the file.
    /// </summary>
    public static bool TryElevatedWipeRequest()
    {
        var path = AppPaths.WipeRequest;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
        try
        {
            var game = JsonSerializer.Deserialize<Game>(File.ReadAllText(path));
            if (game is null) return true;
            if (GamePayload.IsGameRunning(game.InstallPath))
            {
                Log.Info($"Aborting uninstall of {game.Name}, still running");
                return true;
            }
            UninstallHere(game);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Elevated wipe request failed", ex);
            return true;
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    public static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool UninstallHere(Game game)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(game.QuietUninstall))
                RunHidden(game.QuietUninstall);
            else if (IsMsi(game.UninstallString))
                RunHidden(MsiQuiet(game.UninstallString));
            else if (!string.IsNullOrEmpty(game.SteamAppId))
                RemoveSteamGame(game);
            else if (!string.IsNullOrWhiteSpace(game.UninstallString)
                     && !game.UninstallString.StartsWith("steam://", StringComparison.OrdinalIgnoreCase))
                RunHidden(WithSilentFlags(game.UninstallString));
            RemoveInstallDir(game.InstallPath, allowCustom: IsCustom(game) || IsKnownStore(game));
        }
        catch (Exception ex)
        {
            Log.Error($"Silent uninstall failed for {game.Name}", ex);
            return !Detector.IsInstalled(game);
        }

        var stillThere = Detector.IsInstalled(game);
        if (stillThere) Log.Info($"Uninstall did not remove {game.Name}; rule kept");
        else Log.Info($"Uninstalled {game.Name}");
        return !stillThere;
    }

    private static bool RetryElevated(Game game)
    {
        try
        {
            Scheduler.EnsureBackgroundTasks();
            File.WriteAllText(AppPaths.WipeRequest, JsonSerializer.Serialize(game));
            if (!Scheduler.RunWipe())
                StartElevatedWipe();
            var until = Environment.TickCount64 + 180_000;
            while (Environment.TickCount64 < until && Detector.IsInstalled(game))
                Thread.Sleep(500);
        }
        catch (Exception ex)
        {
            Log.Error($"Elevated retry failed for {game.Name}", ex);
        }

        var ok = !Detector.IsInstalled(game);
        if (ok) Log.Info($"Uninstalled {game.Name} (elevated)");
        else Log.Info($"Uninstall did not remove {game.Name}; rule kept");
        return ok;
    }

    private static void StartElevatedWipe()
    {
        var exe = AppPaths.AgentPath();
        if (!File.Exists(exe)) return;
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "--wipe",
            UseShellExecute = true,
            Verb = "runas",
        });
        proc?.WaitForExit(180_000);
    }

    private static bool IsMsi(string command) =>
        (command ?? "").Contains("msiexec", StringComparison.OrdinalIgnoreCase);

    private static string MsiQuiet(string command)
    {
        var cleaned = command.Trim().Trim('"');
        if (!cleaned.Contains("/qn", StringComparison.OrdinalIgnoreCase))
            cleaned += " /qn /norestart";
        return cleaned;
    }

    private static string WithSilentFlags(string command)
    {
        var lowered = command.ToLowerInvariant();
        if (lowered.Contains("/s") || lowered.Contains("/silent") || lowered.Contains("/quiet")
            || lowered.Contains("/qn") || lowered.Contains("-silent"))
            return command;
        return command + " /S";
    }

    private static void RunHidden(string command, int timeoutSeconds = 180)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + command,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (proc is null) return;
        if (!proc.WaitForExit(timeoutSeconds * 1000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }
    }

    private static void RemoveSteamGame(Game game)
    {
        var appId = game.SteamAppId;
        StopNamed("steam", "steamwebhelper");
        var install = !string.IsNullOrEmpty(game.InstallPath)
            ? game.InstallPath
            : Detector.SteamInstallDir(appId);
        if (!string.IsNullOrEmpty(install))
        {
            var steamapps = FindParent(install, "steamapps");
            if (steamapps is not null)
            {
                var manifest = Path.Combine(steamapps, $"appmanifest_{appId}.acf");
                TryDeleteFile(manifest);
            }
            RemoveInstallDir(install, allowCustom: false);
        }
        Log.Info($"Removed Steam files for app {appId}");
    }

    private static string? FindParent(string path, string name)
    {
        var current = new DirectoryInfo(path);
        for (var i = 0; i < 8 && current is not null; i++)
        {
            if (current.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }

    private static bool IsCustom(Game game) =>
        game.Source.Equals("Custom", StringComparison.OrdinalIgnoreCase)
        || game.Id.StartsWith("custom:", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownStore(Game game) =>
        game.Source is "Ubisoft" or "EA" or "Battle.net" or "Xbox" or "Rockstar" or "Riot";

    private static void RemoveInstallDir(string? rawPath, bool allowCustom)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || !Directory.Exists(rawPath)) return;
        if (!IsSafeGameDir(rawPath, allowCustom))
        {
            Log.Info($"Refusing to delete unsafely-looking path: {rawPath}");
            return;
        }
        StopProcessesIn(rawPath);
        foreach (var file in WalkPayloadFiles(rawPath))
        {
            if (GamePayload.ShouldDelete(file))
                TryDeleteFile(file);
        }
        foreach (var dir in WalkPayloadDirs(rawPath).OrderByDescending(d => d.Length))
            TryDeleteDir(dir);
    }

    private static void StopProcessesIn(string root)
    {
        string resolved;
        try { resolved = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar; }
        catch { return; }
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var exe = proc.MainModule?.FileName;
                if (string.IsNullOrEmpty(exe)) continue;
                var full = Path.GetFullPath(exe);
                if (full.StartsWith(resolved, StringComparison.OrdinalIgnoreCase))
                    proc.Kill(entireProcessTree: true);
            }
            catch { /* access / exited */ }
            finally { proc.Dispose(); }
        }
        Thread.Sleep(400);
    }

    private static void StopNamed(params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try { proc.Kill(entireProcessTree: true); }
                catch { /* ignore */ }
                finally { proc.Dispose(); }
            }
        }
        Thread.Sleep(800);
    }

    private static IEnumerable<string> WalkPayloadFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string[] files = [];
            string[] subs = [];
            try { files = Directory.GetFiles(dir); }
            catch (Exception ex) { Log.Warn($"Could not list files in {dir}: {ex.Message}"); }
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception ex) { Log.Warn($"Could not list dirs in {dir}: {ex.Message}"); }
            foreach (var file in files) yield return file;
            foreach (var sub in subs)
            {
                if (!GamePayload.IsSidecarDir(Path.GetFileName(sub)))
                    stack.Push(sub);
            }
        }
    }

    private static IEnumerable<string> WalkPayloadDirs(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        var found = new List<string>();
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            if (!dir.Equals(root, StringComparison.OrdinalIgnoreCase))
                found.Add(dir);
            try
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (!GamePayload.IsSidecarDir(Path.GetFileName(sub)))
                        stack.Push(sub);
                }
            }
            catch { /* ignore */ }
        }
        return found;
    }

    private static void TryDeleteFile(string file)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (!File.Exists(file)) return;
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                return;
            }
            catch when (attempt < 3)
            {
                Thread.Sleep(250);
            }
            catch (Exception ex)
            {
                Log.Warn($"Could not delete {file}: {ex.Message}");
                return;
            }
        }
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: false);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not delete {dir}: {ex.Message}");
        }
    }

    private static bool IsSafeGameDir(string path, bool allowCustom)
    {
        string resolved;
        try { resolved = Path.GetFullPath(path).ToLowerInvariant(); }
        catch { return false; }
        var parts = resolved.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(p => p.Length > 0).ToArray();
        if (parts.Length < 2) return false;
        var windows = (Environment.GetFolderPath(Environment.SpecialFolder.Windows) ?? @"C:\Windows").ToLowerInvariant();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).ToLowerInvariant();
        var trimmed = resolved.TrimEnd('\\');
        if (trimmed == windows.TrimEnd('\\') || trimmed == home.TrimEnd('\\')) return false;
        if (trimmed.EndsWith(@"\windows") || trimmed.EndsWith(@"\program files")
            || trimmed.EndsWith(@"\program files (x86)") || trimmed.EndsWith(@"\users"))
            return false;
        if (SafeMarkers.Any(marker => resolved.Contains(marker))) return true;
        return allowCustom && parts.Length >= 2;
    }
}
