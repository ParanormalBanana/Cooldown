using Cooldown.Models;

namespace Cooldown;

internal static class Worker
{
    /// <summary>
    /// Scheduled uninstalls wait until 05:00 local time on a new calendar date.
    /// That way a PC left on overnight is cleared by morning, without firing at
    /// midnight while someone is still playing. Boot/logon always uninstalls.
    /// </summary>
    internal static readonly TimeSpan DayStartsAt = TimeSpan.FromHours(5);

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

            if (!entry.LastSeenInstalled && entry.ConfirmedClear)
            {
                Rewards.NoteReinstalled(state, entry);
                entry.ConfirmedClear = false;
                changed = true;
            }
            entry.LastSeenInstalled = true;

            if (!ShouldUninstall(active, entry, now)) continue;

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

    private static bool ShouldUninstall(HashSet<string> events, CooldownEntry entry, DateTime now)
    {
        if (events.Contains("startup")) return true;
        if (!entry.ConfirmedClear) return true;
        if (events.Contains("schedule") && DailyDue(entry.LastFiredAt, now)) return true;
        return false;
    }

    internal static bool DailyDue(string lastFiredAt, DateTime now)
    {
        if (now.TimeOfDay < DayStartsAt) return false;
        if (string.IsNullOrEmpty(lastFiredAt) || !DateTime.TryParse(lastFiredAt, out var last))
            return true;
        return last.Date < now.Date;
    }
}
