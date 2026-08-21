using Cooldown.Models;

namespace Cooldown;

internal static class Worker
{
    /// <summary>
    /// Uninstall on put-on (UI), at Windows logon, or at 05:00 via the daily task.
    /// Hourly/schedule scans only update stats. They must never delete files, or a
    /// same-day reinstall gets eaten while someone is playing.
    /// </summary>
    public static void Run(IEnumerable<string> events)
    {
        AppPaths.Ensure();
        Log.Configure();
        var state = Storage.Load();
        var scanned = Detector.Discover(state.DisabledSources);
        var now = DateTime.Now;
        var active = new HashSet<string>(events, StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var entry in state.Cooldowns)
        {
            if (!entry.Enabled) continue;
            if (state.IgnoredIds.Contains(entry.Game.Id, StringComparer.OrdinalIgnoreCase)
                || state.IgnoredNames.Contains(entry.Game.Name, StringComparer.OrdinalIgnoreCase))
                continue;
            var installed = Detector.IsInstalled(entry.Game, scanned);
            if (!installed)
            {
                if (entry.LastSeenInstalled || !entry.ConfirmedClear) changed = true;
                entry.LastSeenInstalled = false;
                entry.ConfirmedClear = true;
                var before = (entry.ClearStreakDays, state.Points, state.Events.Count);
                Rewards.NoteStillClear(state, entry, now.ToString("yyyy-MM-dd"));
                if ((entry.ClearStreakDays, state.Points, state.Events.Count) != before)
                    changed = true;
                continue;
            }

            // Game exe is the install signal. Count a reinstall if it came
            // back after we already uninstalled once.
            if (!entry.LastSeenInstalled && (entry.ConfirmedClear || entry.TotalUninstalls > 0))
            {
                Rewards.NoteReinstalled(state, entry);
                entry.ConfirmedClear = false;
                changed = true;
            }
            entry.LastSeenInstalled = true;

            if (!ShouldUninstall(active)) continue;

            Log.Info($"Cooldown uninstall for {entry.Game.Name} ({string.Join(",", active.OrderBy(x => x))})");
            var ok = Uninstaller.UninstallQuietly(entry.Game);
            entry.LastFiredAt = now.ToString("s");
            changed = true;
            if (ok)
            {
                entry.LastSeenInstalled = false;
                entry.ConfirmedClear = false;
                Rewards.NoteUninstalled(state, entry);
                scanned = scanned.Where(g => g.Id != entry.Game.Id).ToList();
            }
        }

        if (changed) Storage.Save(state);
        Scheduler.EnsureBackgroundTasks(state);
    }

    private static bool ShouldUninstall(HashSet<string> events) =>
        events.Contains("startup");
}
