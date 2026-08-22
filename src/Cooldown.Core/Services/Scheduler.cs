using System.Diagnostics;
using Microsoft.Win32;
using Cooldown.Models;

namespace Cooldown;

internal static class Scheduler
{
    private const string TaskWorker = @"Cooldown\Worker";
    private const string TaskStartup = @"Cooldown\Startup";
    private const string TaskUninstall = @"Cooldown\Uninstall";
    private const string TaskDaily = @"Cooldown\Daily";
    private const string TaskWipe = @"Cooldown\Wipe";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunWorker = "CooldownStartup";
    private const string RunWatch = "CooldownWatch";

    public static void EnsureBackgroundTasks(AppState? state = null)
    {
        _ = state;
        UpsertTask(TaskStartup, AppPaths.AgentCommand("--tray"), "/SC ONLOGON");
        UpsertTask(TaskUninstall, AppPaths.AgentCommand("--now"), "/SC ONLOGON");
        UpsertTask(TaskDaily, AppPaths.AgentCommand("--now"), "/SC DAILY /ST 05:00");
        UpsertTask(TaskWipe, AppPaths.AgentCommand("--wipe"), "/SC ONLOGON");
        DeleteTask(TaskWorker);
        ClearRunKey(RunWorker);
        ClearRunKey(RunWatch);
        TryStartTray();
    }

    public static bool RunWipe() => RunTask(TaskWipe);

    private static void TryStartTray()
    {
        try
        {
            if (Process.GetProcessesByName("Cooldown.Agent").Length > 0) return;
            if (RunTask(TaskStartup)) return;
            var exe = AppPaths.AgentPath();
            if (!File.Exists(exe) || Path.GetFileNameWithoutExtension(exe) != "Cooldown.Agent")
                return;
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--tray",
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not start agent: {ex.Message}");
        }
    }

    private static void ClearRunKey(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
        catch { /* ignore */ }
    }

    private static void UpsertTask(string name, string command, string schedule)
    {
        var tr = command.Replace("\"", "\\\"");
        Schtasks($"/Create /TN \"{name}\" /TR \"{tr}\" {schedule} /RL HIGHEST /F");
    }

    private static bool RunTask(string name) => Schtasks($"/Run /TN \"{name}\"") == 0;

    private static void DeleteTask(string name) =>
        Schtasks($"/Delete /TN \"{name}\" /F", warn: false);

    private static int Schtasks(string arguments, bool warn = true)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (proc is null) return -1;
            proc.WaitForExit(15_000);
            if (warn && proc.ExitCode != 0)
            {
                var err = proc.StandardError.ReadToEnd();
                Log.Warn($"schtasks failed ({proc.ExitCode}): {err.Split('\n').FirstOrDefault()?.Trim()}");
            }
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Error("Scheduled task command raised", ex);
            return -1;
        }
    }
}
