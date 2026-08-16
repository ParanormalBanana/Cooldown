using System.Collections.Concurrent;
using System.IO;
using System.Net;
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
        "https://cdn.akamai.steamstatic.com/steam/apps/{0}/header.jpg",
        "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{0}/header.jpg",
        "https://steamcdn-a.akamaihd.net/steam/apps/{0}/header.jpg",
        "https://cdn.cloudflare.steamstatic.com/steam/apps/{0}/library_600x900.jpg",
        "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{0}/library_600x900.jpg",
        "https://cdn.cloudflare.steamstatic.com/steam/apps/{0}/capsule_616x353.jpg",
        "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{0}/capsule_616x353.jpg",
        "https://cdn.cloudflare.steamstatic.com/steam/apps/{0}/library_hero.jpg",
        "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{0}/library_hero.jpg",
    ];

    private static readonly string[] TailBits =
    [
        "playtest", "play test", "demo", "beta", "alpha",
        "definitive edition", "director's cut", "directors cut",
        "anniversary edition", "windows edition", "complete edition",
        "goty", "game of the year edition", "game of the year",
        "enhanced edition", "deluxe edition", "gold edition", "remastered",
    ];

    public static string FilePath(Game game)
    {
        var safe = Regex.Replace(game.Id, @"[^a-zA-Z0-9._-]+", "_");
        return Path.Combine(AppPaths.CoversDir, safe + ".jpg");
    }

    public static bool ExistsOnDisk(Game game)
    {
        var path = FilePath(game);
        return File.Exists(path) && new FileInfo(path).Length > 500;
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
            for (var i = 0; i < 400; i++)
            {
                await Task.Delay(50, ct);
                if (Memory.TryGetValue(key, out cached)) return cached;
                if (!InFlight.ContainsKey(key)) break;
            }
            if (Memory.TryGetValue(key, out cached)) return cached;
        }
        try
        {
            if (!await FetchFileAsync(game, ct)) return null;
            var path = FilePath(game);
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

    internal static async Task<bool> FetchFileAsync(Game game, CancellationToken ct = default)
    {
        var path = FilePath(game);
        if (!ExistsOnDisk(game))
            await DownloadAsync(game, path, ct);
        return ExistsOnDisk(game);
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
        var tried = new HashSet<string>(StringComparer.Ordinal);
        foreach (var appId in await AppIdsForAsync(game, ct))
        {
            if (string.IsNullOrEmpty(appId) || !tried.Add(appId)) continue;
            if (!await TrySaveHeader(appId, dest, ct))
            {
                ForgetAppIdIf(game.Name, appId);
                continue;
            }
            RememberAppId(game.Name, appId);
            if (string.IsNullOrEmpty(game.SteamAppId)) game.SteamAppId = appId;
            return;
        }
        await TrySaveGog(game, dest, ct);
    }

    private static async Task<IEnumerable<string>> AppIdsForAsync(Game game, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>();
        void Offer(string? id)
        {
            if (!string.IsNullOrEmpty(id) && id.All(char.IsDigit) && seen.Add(id))
                ids.Add(id);
        }

        Offer(Detector.SteamAppIdFor(game));
        foreach (var id in await SearchSteamAsync(game.Name, ct))
            Offer(id);
        foreach (var query in SearchQueries(game.Name))
            Offer(CachedAppId(query));
        return ids;
    }

    private static async Task<bool> TrySaveHeader(string appId, string dest, CancellationToken ct)
    {
        var local = LocalSteamHeader(appId);
        if (local is not null)
        {
            File.Copy(local, dest, overwrite: true);
            return true;
        }
        foreach (var template in HeaderUrls)
        {
            if (await TrySaveUrl(string.Format(template, appId), dest, ct))
                return true;
        }
        return await TrySaveAppDetails(appId, dest, ct);
    }

    private static async Task<bool> TrySaveAppDetails(string appId, string dest, CancellationToken ct)
    {
        try
        {
            var url = "https://store.steampowered.com/api/appdetails?appids=" + appId + "&cc=US&l=english";
            using var response = await Http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty(appId, out var node)) return false;
            if (!node.TryGetProperty("success", out var ok) || !ok.GetBoolean()) return false;
            if (!node.TryGetProperty("data", out var data)) return false;
            foreach (var key in new[] { "header_image", "capsule_image", "capsule_imagev5" })
            {
                if (!data.TryGetProperty(key, out var img)) continue;
                var href = img.GetString();
                if (await TrySaveUrl(href, dest, ct)) return true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Steam details {appId}: {ex.Message}");
        }
        return false;
    }

    private static async Task<bool> TrySaveGog(Game game, string dest, CancellationToken ct)
    {
        if (!game.Id.StartsWith("gog:", StringComparison.OrdinalIgnoreCase)) return false;
        var id = game.Id[4..];
        if (string.IsNullOrEmpty(id) || !id.All(char.IsDigit)) return false;
        try
        {
            var url = "https://api.gog.com/products/" + id + "?expand=screenshots";
            using var response = await Http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return false;
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("images", out var images)
                && images.TryGetProperty("background", out var bg)
                && await TrySaveUrl(AbsUrl(bg.GetString()), dest, ct))
                return true;
            if (root.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array)
            {
                foreach (var shot in shots.EnumerateArray())
                {
                    foreach (var key in new[] { "image_url", "formatter_url" })
                    {
                        if (!shot.TryGetProperty(key, out var href)) continue;
                        if (await TrySaveUrl(AbsUrl(href.GetString()), dest, ct)) return true;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"GOG cover {game.Name}: {ex.Message}");
        }
        return false;
    }

    private static string? AbsUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        if (href.StartsWith("//", StringComparison.Ordinal)) return "https:" + href;
        return href;
    }

    private static async Task<bool> TrySaveUrl(string? url, string dest, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return false;
            if (!response.IsSuccessStatusCode) return false;
            var data = await response.Content.ReadAsByteArrayAsync(ct);
            if (!LooksLikeImage(data, response.Content.Headers.ContentType?.MediaType)) return false;
            await File.WriteAllBytesAsync(dest, data, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Cover download: {ex.Message}");
        }
        return false;
    }

    private static bool LooksLikeImage(byte[] data, string? media)
    {
        if (data.Length < 500) return false;
        if (!string.IsNullOrEmpty(media) && media.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return true;
        return data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8;
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
                     Path.Combine(cache, appId, "library_hero.jpg"),
                     Path.Combine(cache, appId, "library_hero_2x.jpg"),
                     Path.Combine(cache, $"{appId}_library_600x900.jpg"),
                     Path.Combine(cache, appId, "library_600x900.jpg"),
                     Path.Combine(cache, $"{appId}_capsule_616x353.jpg"),
                     Path.Combine(cache, appId, "capsule_616x353.jpg"),
                 })
        {
            if (File.Exists(candidate) && new FileInfo(candidate).Length > 500)
                return candidate;
        }
        var dir = Path.Combine(cache, appId);
        if (!Directory.Exists(dir)) return null;
        try
        {
            return Directory.EnumerateFiles(dir)
                .Select(path => new FileInfo(path))
                .Where(file => file.Length > 4000
                               && file.Extension is ".jpg" or ".jpeg" or ".png" or ".webp")
                .OrderByDescending(file => file.Length)
                .FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static string? CachedAppId(string name)
    {
        var cache = IdCache();
        var key = name.Trim().ToLowerInvariant();
        lock (IdCacheGate)
        {
            return cache.TryGetValue(key, out var hit) && !string.IsNullOrEmpty(hit) ? hit : null;
        }
    }

    private static void RememberAppId(string name, string appId)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(appId)) return;
        var cache = IdCache();
        var key = name.Trim().ToLowerInvariant();
        lock (IdCacheGate)
        {
            cache[key] = appId;
            SaveIdCache(cache);
        }
    }

    private static void ForgetAppIdIf(string name, string appId)
    {
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(appId)) return;
        var cache = IdCache();
        var key = name.Trim().ToLowerInvariant();
        lock (IdCacheGate)
        {
            if (!cache.TryGetValue(key, out var hit) || hit != appId) return;
            cache.Remove(key);
            SaveIdCache(cache);
        }
    }

    private static async Task<List<string>> SearchSteamAsync(string name, CancellationToken ct)
    {
        var ranked = new List<(string Id, int Score, int Length)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queries = SearchQueries(name).ToList();
        var wanted = queries.Select(NormName).Where(s => s.Length > 0).Distinct().ToList();
        try
        {
            foreach (var query in queries)
            {
                foreach (var (id, title) in await SteamHitsAsync(query, ct))
                {
                    if (!seen.Add(id)) continue;
                    if (IsAddonTitle(title, name)) continue;
                    var norm = NormName(title);
                    var score = -1;
                    foreach (var want in wanted)
                    {
                        var s = ScoreTitle(want, norm);
                        if (s < 0) continue;
                        if (score < 0 || s < score) score = s;
                    }
                    if (score < 0) continue;
                    ranked.Add((id, score, norm.Length));
                }
            }
            return ranked
                .OrderBy(x => x.Score)
                .ThenBy(x => x.Length)
                .Select(x => x.Id)
                .Take(12)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Steam search {name}: {ex.Message}");
        }
        return ranked
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Length)
            .Select(x => x.Id)
            .Take(12)
            .ToList();
    }

    private static async Task<List<(string Id, string Title)>> SteamHitsAsync(string query, CancellationToken ct)
    {
        var hits = new List<(string Id, string Title)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? id, string? title)
        {
            if (string.IsNullOrEmpty(id) || !id.All(char.IsDigit) || !seen.Add(id)) return;
            hits.Add((id, title ?? ""));
        }

        try
        {
            var store = "https://store.steampowered.com/api/storesearch/?term="
                        + Uri.EscapeDataString(query) + "&l=english&cc=US";
            using var response = await Http.GetAsync(store, ct);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        var type = item.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                        if (type is "bundle" or "tag") continue;
                        var title = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var id = item.TryGetProperty("id", out var ident) ? ident.ToString() : "";
                        Add(id, title);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Steam store search {query}: {ex.Message}");
        }

        try
        {
            var community = "https://steamcommunity.com/actions/SearchApps/" + Uri.EscapeDataString(query);
            using var response = await Http.GetAsync(community, ct);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        var title = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var id = item.TryGetProperty("appid", out var ident) ? ident.ToString() : "";
                        Add(id, title);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn($"Steam community search {query}: {ex.Message}");
        }

        return hits;
    }

    private static IEnumerable<string> SearchQueries(string name)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Offer(string? value)
        {
            var t = (value ?? "").Trim();
            if (t.Length < 2) return;
            seen.Add(t);
        }

        Offer(name);
        Offer(HyphenToColon(name));
        Offer(PunctSoft(name));
        var stripped = StripSuffixes(name);
        Offer(stripped);
        Offer(HyphenToColon(stripped));
        Offer(PunctSoft(stripped));
        return seen;
    }

    private static string HyphenToColon(string name) =>
        Regex.Replace(name, @"\s+[-–—]\s+", ": ");

    private static string PunctSoft(string name)
    {
        var s = name.Replace("™", " ").Replace("®", " ").Replace("©", " ");
        s = Regex.Replace(s, @"[''`´]", "");
        s = Regex.Replace(s, @"[^a-zA-Z0-9]+", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string StripSuffixes(string name)
    {
        var current = name.Trim();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var bit in TailBits)
            {
                if (!current.EndsWith(bit, StringComparison.OrdinalIgnoreCase)) continue;
                var next = current[..^bit.Length].Trim().TrimEnd('-', ':', '–', '—', ' ').Trim();
                if (next.Length < 2) continue;
                current = next;
                changed = true;
                break;
            }
        }
        return current;
    }

    private static int ScoreTitle(string wanted, string norm)
    {
        if (string.IsNullOrEmpty(wanted) || string.IsNullOrEmpty(norm)) return -1;
        if (norm == wanted) return 0;
        if (norm.StartsWith(wanted, StringComparison.Ordinal))
        {
            var rest = norm[wanted.Length..];
            var score = 10 + rest.Length;
            if (rest.Length > 0 && char.IsDigit(rest[0])) score += 1000;
            return score;
        }
        if (wanted.StartsWith(norm, StringComparison.Ordinal))
        {
            var rest = wanted[norm.Length..];
            if (rest.Length > 0 && rest.All(char.IsDigit)) return -1;
            return 50 + rest.Length;
        }
        return -1;
    }

    private static bool IsAddonTitle(string title, string wanted)
    {
        var t = title.ToLowerInvariant();
        var w = wanted.ToLowerInvariant();
        foreach (var bit in new[] { "soundtrack", " ost", "dlc", "artbook", "upgrade pack", "upgrade to" })
        {
            if (t.Contains(bit) && !w.Contains(bit.Trim())) return true;
        }
        return false;
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
                {
                    _idCache = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                               ?? new Dictionary<string, string>();
                    foreach (var empty in _idCache.Where(kv => string.IsNullOrEmpty(kv.Value)).Select(kv => kv.Key).ToList())
                        _idCache.Remove(empty);
                }
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
