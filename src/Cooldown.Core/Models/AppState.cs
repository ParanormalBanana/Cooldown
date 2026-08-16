using System.Text.Json.Serialization;

namespace Cooldown.Models;

public sealed class RewardEvent
{
    [JsonPropertyName("at")] public string At { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("points")] public int Points { get; set; }
    [JsonPropertyName("game_name")] public string GameName { get; set; } = "";
}

public sealed class AppState
{
    [JsonPropertyName("cooldowns")] public List<CooldownEntry> Cooldowns { get; set; } = [];
    [JsonPropertyName("events")] public List<RewardEvent> Events { get; set; } = [];
    [JsonPropertyName("points")] public int Points { get; set; }
    [JsonPropertyName("lifetime_points")] public int LifetimePoints { get; set; }
    [JsonPropertyName("best_streak_days")] public int BestStreakDays { get; set; }
    [JsonPropertyName("awarded_milestones")] public List<string> AwardedMilestones { get; set; } = [];
    [JsonPropertyName("known_games")] public List<Game> KnownGames { get; set; } = [];
    [JsonPropertyName("custom_games")] public List<Game> CustomGames { get; set; } = [];
    [JsonPropertyName("custom_scan_dirs")] public List<string> CustomScanDirs { get; set; } = [];
    [JsonPropertyName("hidden_ids")] public List<string> HiddenIds { get; set; } = [];
    [JsonPropertyName("ignored_ids")] public List<string> IgnoredIds { get; set; } = [];
    [JsonPropertyName("ignored_names")] public List<string> IgnoredNames { get; set; } = [];
}
