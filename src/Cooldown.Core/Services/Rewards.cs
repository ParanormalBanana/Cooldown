using Cooldown.Models;

namespace Cooldown;

internal static class Rewards
{
    public static void NoteCooldownStarted(AppState state, bool alreadyHasCooldown)
    {
        state.HasRanked = true;
        AddPoints(state, alreadyHasCooldown ? 0 : 100);
    }

    public static void NoteTakenOff(AppState state)
    {
        state.HasRanked = true;
        AddPoints(state, -300);
    }

    public static void NoteUninstalled(AppState state, CooldownEntry entry)
    {
        _ = state;
        entry.TotalUninstalls++;
    }

    public static void NoteStillClear(AppState state, CooldownEntry entry, string today)
    {
        if (entry.LastAwardDate == today) return;
        if (DateTime.TryParse(entry.CreatedAt, out var created) && created.ToString("yyyy-MM-dd") == today)
            return;
        entry.ClearStreakDays = string.IsNullOrEmpty(entry.LastAwardDate) ? 1 : entry.ClearStreakDays + 1;
        entry.LastAwardDate = today;
        entry.BestStreakDays = Math.Max(entry.BestStreakDays, entry.ClearStreakDays);
        state.BestStreakDays = Math.Max(state.BestStreakDays, entry.ClearStreakDays);
        AddPoints(state, DailyPoints(state, entry));
    }

    public static void NoteReinstalled(AppState state, CooldownEntry entry)
    {
        AddPoints(state, -200);
        entry.ClearStreakDays = 0;
        entry.LastAwardDate = "";
        state.HasRanked = true;
    }

    public static string Rank(AppState state)
    {
        if (!state.HasRanked) return "Unranked";
        var score = state.Points;
        if (score <= -101) return "F";
        if (score <= 0) return "E";
        if (score <= 100) return "D";
        if (score <= 200) return "C";
        if (score <= 300) return "B";
        if (score <= 600) return "A";
        return "S";
    }

    public static int CurrentStreak(AppState state) =>
        state.Cooldowns
            .Where(entry => entry.Enabled && !entry.LastSeenInstalled)
            .Select(entry => entry.ClearStreakDays)
            .DefaultIfEmpty(0)
            .Max();

    private static int DailyPoints(AppState state, CooldownEntry entry)
    {
        var uninstalled = state.Cooldowns
            .Where(item => item.Enabled && !item.LastSeenInstalled)
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToList();
        var primary = uninstalled.FirstOrDefault();
        return primary?.Id == entry.Id ? 100 : 50;
    }

    private static void AddPoints(AppState state, int points)
    {
        if (points == 0) return;
        state.Points += points;
        state.LifetimePoints += points;
        if (state.HasRanked == false && points != 0)
            state.HasRanked = true;
    }
}
