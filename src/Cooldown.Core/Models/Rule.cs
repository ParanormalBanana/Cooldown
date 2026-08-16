using System.Text.Json.Serialization;

namespace Cooldown.Models;

public sealed class CooldownEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("game")] public Game Game { get; set; } = new();
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = "";
    [JsonPropertyName("last_fired_at")] public string LastFiredAt { get; set; } = "";
    [JsonPropertyName("last_award_date")] public string LastAwardDate { get; set; } = "";
    [JsonPropertyName("last_seen_installed")] public bool LastSeenInstalled { get; set; } = true;
    [JsonPropertyName("clear_streak_days")] public int ClearStreakDays { get; set; }
    [JsonPropertyName("best_streak_days")] public int BestStreakDays { get; set; }
    [JsonPropertyName("total_uninstalls")] public int TotalUninstalls { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
}

public static class Milestones
{
    public static readonly (int Days, int Points, string Title)[] All =
    [
        (1, 25, "First quiet day"),
        (3, 50, "Cooling off"),
        (7, 100, "One week clear"),
        (14, 150, "Two weeks of space"),
        (30, 300, "A full month"),
        (90, 500, "A season of quiet"),
    ];
}
