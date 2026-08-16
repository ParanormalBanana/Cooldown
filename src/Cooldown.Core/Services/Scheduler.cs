using System.Diagnostics;
using Microsoft.Win32;
using Cooldown.Models;

namespace Cooldown;

internal static class Scheduler
{
    private const string TaskWorker = @"Cooldown\Worker";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunWorker = "CooldownStartup";
    private const string RunWatch = "CooldownWatch";

    public static void EnsureBackgroundTasks(AppState state)
    {
        _ = state;
        var workerCmd = AppPaths.AgentCommand("--worker");
        var startupCmd = AppPaths.AgentCommand("--worker", "--startup");

        UpsertHourlyTask(TaskWorker, workerCmd);
        SetRunKey(RunWorker, startupCmd);
        ClearRunKey(RunWatch);
    }

    private static void SetRunKey(string name, string command)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key?.SetValue(name, command);
        }
        catch (Exception ex)
        {
            Log.Error($"Could not write Run key {name}", ex);
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

    private static void UpsertHourlyTask(string name, string command)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/Create /TN \"{name}\" /TR \"{command.Replace("\"", "\\\"")}\" /SC HOURLY /F",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (proc is null) return;
            proc.WaitForExit(15_000);
            if (proc.ExitCode != 0)
            {
                var err = proc.StandardError.ReadToEnd();
                Log.Warn($"schtasks failed ({proc.ExitCode}): {err.Split('\n').FirstOrDefault()?.Trim()}");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Scheduled task command raised", ex);
        }
    }
}
