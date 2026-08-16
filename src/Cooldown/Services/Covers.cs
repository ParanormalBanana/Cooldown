using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cooldown.Models;

namespace Cooldown;

internal static class Covers
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly ConcurrentDictionary<string, ImageSource> Memory = new();
    private static readonly ConcurrentDictionary<string, byte> InFlight = new();
    private static readonly object IdCacheGate = new();
    private static Dictionary<string, string>? _idCache;

    private static readonly string[] HeaderUrls =
    [
        "https://cdn.cloudflare.steamstatic.com/steam/apps/{0}/header.jpg",
        "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{0}/header.jpg",
    ];

    public static string FilePath(Game game)
    {
        var safe = Regex.Replace(game.Id, @"[^a-zA-Z0-9._-]+", "_");
        return Path.Combine(AppPaths.CoversDir, safe + ".jpg");
    }

    public static bool ExistsOnDisk(Game game)
    {
        var path = FilePath(game);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    public static ImageSource? TryGetCached(Game game, int decodeWidth)
    {
        var key = $"{game.Id}:{decodeWidth}";
        return Memory.TryGetValue(key, out var img) ? img : null;
    }

    public static async Task<ImageSource?> EnsureAsync(Game game, int decodeWidth, CancellationToken ct = default)
    {
        var key = $"{game.Id}:{decodeWidth}";
        if (Memory.TryGetValue(key, out var cached)) return cached;
        if (!InFlight.TryAdd(key, 0))
        {
            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(50, ct);
                if (Memory.TryGetValue(key, out cached)) return cached;
            }
        }
        try
        {
            var path = FilePath(game);
            if (!ExistsOnDisk(game))
                await DownloadAsync(game, path, ct);
            if (!ExistsOnDisk(game)) return null;
            var image = await Task.Run(() => LoadBitmap(path, decodeWidth), ct);
            if (image is not null) Memory[key] = image;
            return image;
        }
        catch (Exception ex)
        {
            Log.Warn($"Cover failed for {game.Name}: {ex.Message}");
            return null;
        }
        finally
        {
            InFlight.TryRemove(key, out _);
        }
    }

    private static BitmapImage? LoadBitmap(string path, int decodeWidth)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = Math.Max(80, decodeWidth);
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static async Task DownloadAsync(Game game, string dest, CancellationToken ct)
    {
        var appId = game.SteamAppId;
        if (string.IsNullOrEmpty(appId))
            appId = await LookupAppIdAsync(game.Name, ct);
        if (string.IsNullOrEmpty(appId)) return;

        var local = LocalSteamHeader(appId);
        if (local is not null)
        {
            File.Copy(local, dest, overwrite: true);
            return;
        }

        foreach (var template in HeaderUrls)
        {
            try
            {
                using var response = await Http.GetAsync(string.Format(template, appId), ct);
                if (!response.IsSuccessStatusCode) continue;
                var data = await response.Content.ReadAsByteArrayAsync(ct);
                if (data.Length < 500) continue;
                await File.WriteAllBytesAsync(dest, data, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Warn($"Cover download {appId}: {ex.Message}");
            }
        }
    }

    private static string? LocalSteamHeader(string appId)
    {
        var steam = Detector.SteamRoot();
        if (steam is null) return null;
        var cache = Path.Combine(steam, "appcache", "librarycache");
        foreach (var candidate in new[]
                 {
                     Path.Combine(cache, $"{appId}_header.jpg"),
                     Path.Combine(cache, appId, "header.jpg"),
                     Path.Combine(cache, $"{appId}_library_hero.jpg"),
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static async Task<string> LookupAppIdAsync(string name, CancellationToken ct)
    {
        var cache = IdCache();
        var key = name.Trim().ToLowerInvariant();
        lock (IdCacheGate)
        {
            if (cache.TryGetValue(key, out var hit)) return hit ?? "";
        }
        var appId = await SearchSteamAsync(name, ct);
        lock (IdCacheGate)
        {
            cache[key] = appId;
            SaveIdCache(cache);
        }
        return appId;
    }

    private static async Task<string> SearchSteamAsync(string name, CancellationToken ct)
    {
        try
        {
            var url = "https://store.steampowered.com/api/storesearch/?term="
                      + Uri.EscapeDataString(name) + "&l=english&cc=US";
            using var response = await Http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return "";
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return "";
            var wanted = NormName(name);
            foreach (var item in items.EnumerateArray())
            {
                var title = NormName(item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "");
                if (title == wanted || wanted.Contains(title) || title.Contains(wanted))
                    return item.TryGetProperty("id", out var id) ? id.ToString() : "";
            }
            if (items.GetArrayLength() > 0)
            {
                var first = items[0];
                return first.TryGetProperty("id", out var id) ? id.ToString() : "";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Steam search {name}: {ex.Message}");
        }
        return "";
    }

    private static string NormName(string value) =>
        Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "");

    private static Dictionary<string, string> IdCache()
    {
        lock (IdCacheGate)
        {
            if (_idCache is not null) return _idCache;
            var path = Path.Combine(AppPaths.CoversDir, "steam_ids.json");
            try
            {
                if (File.Exists(path))
                    _idCache = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                               ?? new Dictionary<string, string>();
            }
            catch { /* ignore */ }
            _idCache ??= new Dictionary<string, string>();
            return _idCache;
        }
    }

    private static void SaveIdCache(Dictionary<string, string> cache)
    {
        try
        {
            var path = Path.Combine(AppPaths.CoversDir, "steam_ids.json");
            File.WriteAllText(path, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignore */ }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 Cooldown/1.0");
        return client;
    }
}
