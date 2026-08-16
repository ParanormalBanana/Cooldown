using System.Text.Json.Serialization;

namespace Cooldown.Models;

public sealed class Game
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "Unknown";
    [JsonPropertyName("source")] public string Source { get; set; } = "Windows";
    [JsonPropertyName("install_path")] public string InstallPath { get; set; } = "";
    [JsonPropertyName("uninstall_string")] public string UninstallString { get; set; } = "";
    [JsonPropertyName("quiet_uninstall")] public string QuietUninstall { get; set; } = "";
    [JsonPropertyName("steam_appid")] public string SteamAppId { get; set; } = "";
    [JsonPropertyName("publisher")] public string Publisher { get; set; } = "";
    [JsonPropertyName("size_bytes")] public long SizeBytes { get; set; }
}
