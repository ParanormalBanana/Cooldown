using Cooldown.Models;

namespace Cooldown;

internal static class Rewards
{
    public static void NoteCooldownStarted(AppState state, bool alreadyHasCooldown, string gameName)
    {
        state.HasRanked = true;
        AddPoints(state, alreadyHasCooldown ? 10 : 100, "cooldown",
            $"Put {gameName} on cooldown", gameName);
    }

    public static void NoteTakenOff(AppState state, CooldownEntry entry)
    {
        var stats = EnsureStats(state, entry.Game);
        stats.LastCooldownDays = DaysOnCooldown(entry.CreatedAt);
        state.HasRanked = true;
        AddPoints(state, -300, "takeoff", $"Took {entry.Game.Name} off cooldown", entry.Game.Name);
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
        var points = DailyPoints(state, entry);
        AddPoints(state, points, "daily", $"Another day off {entry.Game.Name}", entry.Game.Name);
    }

    public static void NoteReinstalled(AppState state, CooldownEntry entry)
    {
        EnsureStats(state, entry.Game).Reinstalls++;
        AddPoints(state, -200, "reinstall", $"Reinstalled {entry.Game.Name}", entry.Game.Name);
        entry.ClearStreakDays = 0;
        entry.LastAwardDate = "";
        state.HasRanked = true;
    }

    public static int DaysOnCooldown(string createdAt)
    {
        if (!DateTime.TryParse(createdAt, out var created)) return 0;
        return Math.Max(0, (DateTime.Now.Date - created.Date).Days);
    }

    public static GameStats EnsureStats(AppState state, Game game)
    {
        var hit = FindStats(state, game);
        if (hit is not null)
        {
            if (!string.Equals(hit.Id, game.Id, StringComparison.OrdinalIgnoreCase))
                hit.Id = game.Id;
            return hit;
        }
        hit = new GameStats { Id = game.Id };
        state.GameStats.Add(hit);
        return hit;
    }

    public static GameStats EnsureStats(AppState state, string gameName)
    {
        var game = state.Cooldowns.FirstOrDefault(c =>
                       string.Equals(c.Game.Name, gameName, StringComparison.OrdinalIgnoreCase))?.Game
                   ?? state.KnownGames.FirstOrDefault(g =>
                       string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase))
                   ?? state.CustomGames.FirstOrDefault(g =>
                       string.Equals(g.Name, gameName, StringComparison.OrdinalIgnoreCase));
        if (game is not null) return EnsureStats(state, game);
        var orphan = state.GameStats.FirstOrDefault(s =>
            string.Equals(s.Id, gameName, StringComparison.OrdinalIgnoreCase));
        if (orphan is not null) return orphan;
        orphan = new GameStats { Id = gameName };
        state.GameStats.Add(orphan);
        return orphan;
    }

    public static GameStats? FindStats(AppState state, Game game)
    {
        var byId = state.GameStats.FirstOrDefault(s =>
            string.Equals(s.Id, game.Id, StringComparison.OrdinalIgnoreCase));
        if (byId is not null) return byId;
        var byName = state.GameStats.FirstOrDefault(s =>
            string.Equals(s.Id, game.Name, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return byName;
        foreach (var stats in state.GameStats)
        {
            var other = GameForStatsId(state, stats.Id);
            if (other is not null && Detector.SameGame(game, other))
                return stats;
        }
        return null;
    }

    private static Game? GameForStatsId(AppState state, string id) =>
        state.Cooldowns.Select(c => c.Game).FirstOrDefault(g =>
            string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? state.KnownGames.FirstOrDefault(g =>
            string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? state.CustomGames.FirstOrDefault(g =>
            string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase));

    public static string Rank(AppState state) => RankFromScore(state.Points, state.HasRanked);

    public static string RankFromScore(int score, bool ranked)
    {
        if (!ranked) return "Unranked";
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

    private static void AddPoints(AppState state, int points, string kind, string message, string gameName)
    {
        if (points == 0) return;
        state.Points += points;
        state.LifetimePoints += points;
        state.HasRanked = true;
        if (!string.IsNullOrEmpty(gameName))
            EnsureStats(state, gameName).Points += points;
        state.Events.Add(new RewardEvent
        {
            At = DateTime.Now.ToString("s"),
            Kind = kind,
            Message = message,
            Points = points,
            GameName = gameName,
        });
    }
}
