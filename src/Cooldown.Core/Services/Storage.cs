using System.Text.Json;
using Cooldown.Models;

namespace Cooldown;

internal static class Storage
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppState Load()
    {
        lock (Gate)
        {
            if (!File.Exists(AppPaths.DataFile))
                return new AppState();
            try
            {
                var raw = File.ReadAllText(AppPaths.DataFile);
                var state = JsonSerializer.Deserialize<AppState>(raw, JsonOptions) ?? new AppState();
                if (state.Cooldowns.Count == 0)
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("rules", out var rules)
                        && rules.ValueKind == JsonValueKind.Array)
                    {
                        state.Cooldowns = JsonSerializer.Deserialize<List<CooldownEntry>>(
                            rules.GetRawText(), JsonOptions) ?? [];
                    }
                }
                foreach (var entry in state.Cooldowns)
                {
                    if (string.IsNullOrEmpty(entry.LastFiredAt))
                        entry.LastFiredAt = string.IsNullOrEmpty(entry.CreatedAt)
                            ? DateTime.Now.ToString("s")
                            : entry.CreatedAt;
                }
                MigrateScore(state);
                var statsWereEmpty = state.GameStats.Count == 0;
                MigrateGameStats(state);
                if (statsWereEmpty && state.GameStats.Count > 0)
                {
                    try { Write(state); }
                    catch (Exception ex) { Log.Error("Could not save game stats", ex); }
                }
                return state;
            }
            catch (Exception ex)
            {
                Log.Error("Could not load state", ex);
                return new AppState();
            }
        }
    }

    private static void MigrateScore(AppState state)
    {
        if (state.ScoreVersion >= 2) return;
        var hadCooldown = state.Cooldowns.Count > 0;
        var hadScore = state.Points != 0 || state.Events.Count > 0;
        state.ScoreVersion = 2;
        state.Points = hadCooldown ? 100 : 0;
        state.LifetimePoints = state.Points;
        state.Events.Clear();
        state.AwardedMilestones.Clear();
        state.HasRanked = hadCooldown || hadScore;
    }

    private static void MigrateGameStats(AppState state)
    {
        if (state.GameStats.Count > 0) return;
        foreach (var ev in state.Events)
        {
            if (string.IsNullOrWhiteSpace(ev.GameName)) continue;
            var stats = Rewards.EnsureStats(state, ev.GameName);
            stats.Points += ev.Points;
            if (string.Equals(ev.Kind, "reinstall", StringComparison.OrdinalIgnoreCase))
                stats.Reinstalls++;
        }
        foreach (var entry in state.Cooldowns)
        {
            var stats = Rewards.EnsureStats(state, entry.Game);
            stats.LastCooldownDays = Rewards.DaysOnCooldown(entry.CreatedAt);
        }
    }

    public static void Save(AppState state)
    {
        lock (Gate)
        {
            try { Write(state); }
            catch (Exception ex) { Log.Error("Could not save state", ex); }
        }
    }

    private static void Write(AppState state)
    {
        if (state.Events.Count > 200)
            state.Events = state.Events.Skip(state.Events.Count - 200).ToList();
        var tmp = AppPaths.DataFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOptions));
        File.Copy(tmp, AppPaths.DataFile, overwrite: true);
        File.Delete(tmp);
    }
}
