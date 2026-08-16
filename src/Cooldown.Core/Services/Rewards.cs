using Cooldown.Models;

namespace Cooldown;

internal static class Rewards
{
    public static void NoteUninstalled(AppState state, CooldownEntry entry)
    {
        entry.TotalUninstalls++;
        AddPoints(state, 0, "uninstall",
            $"{entry.Game.Name} was removed quietly. It stays on cooldown.",
            entry.Game.Name);
    }

    public static void NoteStillClear(AppState state, CooldownEntry entry, string today)
    {
        if (entry.LastAwardDate == today) return;
        entry.ClearStreakDays = string.IsNullOrEmpty(entry.LastAwardDate) ? 1 : entry.ClearStreakDays + 1;
        entry.LastAwardDate = today;
        entry.BestStreakDays = Math.Max(entry.BestStreakDays, entry.ClearStreakDays);
        state.BestStreakDays = Math.Max(state.BestStreakDays, entry.ClearStreakDays);
        AddPoints(state, 10, "streak", StreakMessage(entry), entry.Game.Name);
        MaybeMilestone(state, entry);
    }

    public static void NoteReinstalled(AppState state, CooldownEntry entry)
    {
        if (entry.ClearStreakDays > 0)
        {
            AddPoints(state, 0, "reinstall",
                $"{entry.Game.Name} is back. Cooldown will try again at startup or tomorrow morning.",
                entry.Game.Name);
        }
        entry.ClearStreakDays = 0;
        entry.LastAwardDate = "";
    }

    private static void AddPoints(AppState state, int points, string kind, string message, string gameName)
    {
        if (points != 0)
        {
            state.Points += points;
            state.LifetimePoints += points;
        }
        state.Events.Add(new RewardEvent
        {
            At = DateTime.Now.ToString("s"),
            Kind = kind,
            Message = message,
            Points = points,
            GameName = gameName,
        });
        if (state.Events.Count > 200)
            state.Events = state.Events.Skip(state.Events.Count - 200).ToList();
    }

    private static string StreakMessage(CooldownEntry entry)
    {
        var days = entry.ClearStreakDays;
        return days switch
        {
            1 => $"Nice start. {entry.Game.Name} stayed uninstalled for a day.",
            7 => $"A full week without {entry.Game.Name}. That is real space.",
            30 => $"A month clear of {entry.Game.Name}. That habit is loosening.",
            _ => $"{entry.Game.Name} has stayed uninstalled for {days} days.",
        };
    }

    private static void MaybeMilestone(AppState state, CooldownEntry entry)
    {
        foreach (var (days, points, title) in Milestones.All)
        {
            var key = $"{entry.Id}:{days}";
            if (entry.ClearStreakDays < days || state.AwardedMilestones.Contains(key)) continue;
            state.AwardedMilestones.Add(key);
            var suffix = days == 1 ? "" : "s";
            AddPoints(state, points, "milestone",
                $"{title}: {entry.Game.Name} has been gone for {days} day{suffix}.",
                entry.Game.Name);
        }
    }
}
