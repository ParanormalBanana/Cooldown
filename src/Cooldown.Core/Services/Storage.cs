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
                return state;
            }
            catch (Exception ex)
            {
                Log.Error("Could not load state", ex);
                return new AppState();
            }
        }
    }

    public static void Save(AppState state)
    {
        lock (Gate)
        {
            try
            {
                if (state.Events.Count > 200)
                    state.Events = state.Events.Skip(state.Events.Count - 200).ToList();
                var tmp = AppPaths.DataFile + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOptions));
                File.Copy(tmp, AppPaths.DataFile, overwrite: true);
                File.Delete(tmp);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save state", ex);
            }
        }
    }
}
