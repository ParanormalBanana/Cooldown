using Microsoft.Win32;
using Cooldown.Models;

namespace Cooldown;

internal static class Detector
{
    private static readonly HashSet<string> Publishers = new(StringComparer.OrdinalIgnoreCase)
    {
        "2k", "activision", "annapurna", "bandai namco", "bethesda", "blizzard", "capcom",
        "cd projekt", "devolver", "electronic arts", "epic games", "fromsoftware", "gog.com",
        "microsoft studios", "mojang", "nintendo", "paradox", "riot games", "rockstar", "sega",
        "square enix", "ubisoft", "valve", "xbox game studios",
    };

    private static readonly string[] SkipBits =
    [
        "redistributable", "directx", "vcredist", "visual c++", "runtime", "launcher",
        "prerequisites", "microsoft visual", "online services", "overlay", "web helper",
        "steamworks", "plugin", " driver", "sdk", "cheat", "quixel", "realitycapture",
        "unreal engine",
    ];

    private static readonly HashSet<string> SkipExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "epic games launcher", "epic online services", "gog galaxy", "ubisoft connect",
        "ea app", "origin", "battle.net", "xbox", "xbox app",
    };

    private static readonly Dictionary<string, int> SourceRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Steam"] = 0, ["Epic"] = 1, ["GOG"] = 2, ["Windows"] = 3, ["Custom"] = 4,
    };

    private static readonly string[] JunkExeBits =
    [
        "uninstall", "unins000", "unitycrash", "crashhandler", "crashreport",
        "vcredist", "dxsetup", "redist", "easyanticheat", "eosbootstrapper",
        "overlay", "cefsharp", "webhelper",
    ];

    public static List<Game> Discover()
    {
        var found = new List<Game>();
        found.AddRange(SteamGames());
        found.AddRange(EpicGames());
        found.AddRange(GogGames());
        found.AddRange(RegistryGames());
        return Combine(found);
    }

    public static List<Game> Combine(params IEnumerable<Game>[] batches)
    {
        var found = new List<Game>();
        foreach (var batch in batches)
            found.AddRange(batch);
        return Dedupe(found);
    }

    public static Game FromFolder(string path, string? name = null)
    {
        var full = Path.GetFullPath(path);
        var folderName = string.IsNullOrWhiteSpace(name) ? new DirectoryInfo(full).Name : name.Trim();
        return new Game
        {
            Id = CustomId(full),
            Name = folderName,
            Source = "Custom",
            InstallPath = full,
        };
    }

    public static string CustomId(string path) => "custom:" + Norm(path);

    public static List<Game> ScanDirectory(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return [];
        var games = new List<Game>();
        try
        {
            foreach (var child in Directory.GetDirectories(root))
            {
                if (LooksLikeGameFolder(child))
                    games.Add(FromFolder(child));
            }
            if (games.Count == 0 && LooksLikeGameFolder(root))
                games.Add(FromFolder(root));
        }
        catch (Exception ex)
        {
            Log.Warn($"Custom scan skipped ({root}): {ex.Message}");
        }
        return games;
    }

    public static bool IsInstalled(Game game, IReadOnlyList<Game>? scanned = null)
    {
        scanned ??= Discover();
        if (scanned.Any(item => string.Equals(item.Id, game.Id, StringComparison.OrdinalIgnoreCase)))
            return true;
        var key = SteamAppKey(game);
        if (!string.IsNullOrEmpty(key))
        {
            if (scanned.Any(item =>
                    SteamAppKey(item) == key
                    && (item.Source.Equals("Steam", StringComparison.OrdinalIgnoreCase)
                        || item.Id.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))))
                return true;
            if (SteamManifestPath(key) is not null)
                return true;
        }
        var path = (game.InstallPath ?? "").Trim();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return false;
        try
        {
            return Directory.EnumerateFileSystemEntries(path).Any();
        }
        catch
        {
            return Directory.Exists(path);
        }
    }

    /// <summary>
    /// Steam, Windows leftovers, and custom copies of the same title.
    /// </summary>
    public static bool SameGame(Game a, Game b, bool names = true)
    {
        if (string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase))
            return true;
        var steamA = SteamAppKey(a);
        var steamB = SteamAppKey(b);
        if (!string.IsNullOrEmpty(steamA) && steamA == steamB)
            return true;
        var pathA = Norm(a.InstallPath);
        var pathB = Norm(b.InstallPath);
        if (!string.IsNullOrEmpty(pathA) && pathA == pathB)
            return true;
        return names
            && !string.IsNullOrEmpty(a.Name)
            && string.Equals(Norm(a.Name), Norm(b.Name), StringComparison.Ordinal);
    }

    private static string? SteamAppKey(Game game)
    {
        if (!string.IsNullOrEmpty(game.SteamAppId) && game.SteamAppId.All(char.IsDigit))
            return game.SteamAppId;
        if (game.Id.StartsWith("steam:", StringComparison.OrdinalIgnoreCase))
        {
            var id = game.Id[6..];
            if (id.Length > 0 && id.All(char.IsDigit)) return id;
        }
        if (game.Id.StartsWith("win:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = game.Id[4..];
            const string prefix = "Steam App ";
            if (rest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var id = rest[prefix.Length..].Trim();
                if (id.Length > 0 && id.All(char.IsDigit)) return id;
            }
        }
        var uninstall = game.UninstallString ?? "";
        const string token = "steam://uninstall/";
        var at = uninstall.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            var start = at + token.Length;
            var end = start;
            while (end < uninstall.Length && char.IsDigit(uninstall[end])) end++;
            if (end > start) return uninstall[start..end];
        }
        return null;
    }

    public static string? SteamInstallDir(string appId)
    {
        var manifest = SteamManifestPath(appId);
        if (manifest is null) return null;
        try
        {
            var data = Vdf.Parse(File.ReadAllText(manifest));
            var state = Vdf.Child(data, "AppState") ?? data;
            var installDir = Vdf.Get(state, "installdir").Trim();
            if (string.IsNullOrEmpty(installDir)) return null;
            return Path.Combine(Path.GetDirectoryName(manifest)!, "common", installDir);
        }
        catch
        {
            return null;
        }
    }

    public static string? SteamRoot()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var value = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(value) && Directory.Exists(value))
                return value;
        }
        catch { /* fall through */ }
        const string fallback = @"C:\Program Files (x86)\Steam";
        return Directory.Exists(fallback) ? fallback : null;
    }

    private static List<Game> SteamGames()
    {
        var steamRoot = SteamRoot();
        if (steamRoot is null) return [];
        var games = new List<Game>();
        foreach (var library in SteamLibraries(steamRoot))
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            string[] manifests;
            try { manifests = Directory.GetFiles(steamapps, "appmanifest_*.acf"); }
            catch { continue; }
            foreach (var manifest in manifests)
            {
                try
                {
                    var data = Vdf.Parse(File.ReadAllText(manifest));
                    var state = Vdf.Child(data, "AppState") ?? data;
                    var appId = Vdf.Get(state, "appid");
                    var name = Vdf.Get(state, "name").Trim();
                    var installDir = Vdf.Get(state, "installdir").Trim();
                    if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name) || ShouldSkip(name))
                        continue;
                    var installPath = string.IsNullOrEmpty(installDir)
                        ? ""
                        : Path.Combine(steamapps, "common", installDir);
                    long.TryParse(Vdf.Get(state, "SizeOnDisk"), out var size);
                    games.Add(new Game
                    {
                        Id = $"steam:{appId}",
                        Name = name,
                        Source = "Steam",
                        InstallPath = installPath,
                        SteamAppId = appId,
                        Publisher = name.StartsWith("steam", StringComparison.OrdinalIgnoreCase) ? "Valve" : "",
                        SizeBytes = size,
                        UninstallString = $"steam://uninstall/{appId}",
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn($"Steam manifest skipped ({manifest}): {ex.Message}");
                }
            }
        }
        return games;
    }

    private static List<string> SteamLibraries(string steamRoot)
    {
        var libraries = new List<string> { steamRoot };
        foreach (var candidate in new[]
                 {
                     Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
                     Path.Combine(steamRoot, "config", "libraryfolders.vdf"),
                 })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var parsed = Vdf.Parse(File.ReadAllText(candidate));
                var folders = Vdf.Child(parsed, "libraryfolders") ?? parsed;
                foreach (var node in folders.Values)
                {
                    if (node is Dictionary<string, object> dict)
                    {
                        var p = Vdf.Get(dict, "path");
                        if (!string.IsNullOrEmpty(p)) libraries.Add(p);
                    }
                    else if (node is string s && !string.IsNullOrEmpty(s) && s is not "{" and not "}")
                    {
                        libraries.Add(s);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"libraryfolders.vdf: {ex.Message}");
            }
        }
        var unique = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries)
        {
            var key = Norm(library);
            if (!seen.Add(key)) continue;
            unique.Add(library);
        }
        return unique;
    }

    private static string? SteamManifestPath(string appId)
    {
        var steamRoot = SteamRoot();
        if (steamRoot is null) return null;
        foreach (var library in SteamLibraries(steamRoot))
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
            if (File.Exists(manifest)) return manifest;
        }
        return null;
    }

    private static List<Game> EpicGames()
    {
        var manifests = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) return [];
        var games = new List<Game>();
        foreach (var item in Directory.EnumerateFiles(manifests, "*.item"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(item));
                var root = doc.RootElement;
                var name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() ?? "" : "";
                var appName = root.TryGetProperty("AppName", out var a) ? a.GetString() ?? "" : "";
                var location = root.TryGetProperty("InstallLocation", out var loc) ? loc.GetString() ?? "" : "";
                var incomplete = root.TryGetProperty("bIsIncompleteInstall", out var inc) && inc.ValueKind == System.Text.Json.JsonValueKind.True;
                if (string.IsNullOrWhiteSpace(name) || incomplete || ShouldSkip(name)) continue;
                var uninstall = "";
                if (root.TryGetProperty("UninstallCmdLine", out var u)) uninstall = u.GetString() ?? "";
                if (string.IsNullOrEmpty(uninstall) && root.TryGetProperty("LaunchExecutable", out var l))
                    uninstall = l.GetString() ?? "";
                var publisher = root.TryGetProperty("CatalogNamespace", out var p) ? p.GetString() ?? "Epic Games" : "Epic Games";
                games.Add(new Game
                {
                    Id = $"epic:{(string.IsNullOrEmpty(appName) ? name : appName)}",
                    Name = name,
                    Source = "Epic",
                    InstallPath = location,
                    UninstallString = uninstall,
                    Publisher = publisher,
                });
            }
            catch { /* skip bad manifest */ }
        }
        return games;
    }

    private static List<Game> GogGames()
    {
        var games = new List<Game>();
        foreach (var (hive, path, view) in GogKeySpecs())
        {
            using var root = Open(hive, path, view);
            if (root is null) continue;
            foreach (var subName in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(subName);
                if (sub is null) continue;
                var name = Read(sub, "gameName") ?? Read(sub, "GAMENAME");
                if (string.IsNullOrEmpty(name)) continue;
                games.Add(new Game
                {
                    Id = $"gog:{subName}",
                    Name = name,
                    Source = "GOG",
                    InstallPath = Read(sub, "path") ?? Read(sub, "PATH") ?? "",
                    UninstallString = Read(sub, "uninstallCommand") ?? Read(sub, "UNINSTALL") ?? "",
                    Publisher = "GOG.com",
                });
            }
        }
        return games;
    }

    private static List<Game> RegistryGames()
    {
        var games = new List<Game>();
        foreach (var (hive, path, view) in UninstallKeySpecs())
        {
            using var root = Open(hive, path, view);
            if (root is null) continue;
            foreach (var subName in root.GetSubKeyNames())
            {
                try
                {
                    using var sub = root.OpenSubKey(subName);
                    if (sub is null) continue;
                    var name = Read(sub, "DisplayName");
                    if (string.IsNullOrEmpty(name) || ShouldSkip(name)) continue;
                    var publisher = Read(sub, "Publisher") ?? "";
                    var install = Read(sub, "InstallLocation") ?? "";
                    var uninstall = Read(sub, "UninstallString") ?? "";
                    var quiet = Read(sub, "QuietUninstallString") ?? "";
                    if (!LooksLikeGame(name, publisher, install)) continue;
                    long.TryParse(Read(sub, "EstimatedSize"), out var sizeKb);
                    games.Add(new Game
                    {
                        Id = $"win:{subName}",
                        Name = name,
                        Source = "Windows",
                        InstallPath = install,
                        UninstallString = uninstall,
                        QuietUninstall = quiet,
                        Publisher = publisher,
                        SizeBytes = sizeKb * 1024,
                    });
                }
                catch { /* skip */ }
            }
        }
        return games;
    }

    private static bool LooksLikeGameFolder(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name) || ShouldSkip(name)) return false;
        return CandidateExes(path).Any(file => !IsJunkExe(Path.GetFileName(file)));
    }

    private static IEnumerable<string> CandidateExes(string root)
    {
        foreach (var file in SafeFiles(root, SearchOption.TopDirectoryOnly))
            yield return file;
        foreach (var sub in new[] { "bin", "Bin", "Binaries", "Win64", "x64", "Game" })
        {
            var dir = Path.Combine(root, sub);
            foreach (var file in SafeFiles(dir, SearchOption.TopDirectoryOnly))
                yield return file;
        }
        foreach (var file in SafeFiles(Path.Combine(root, "Binaries", "Win64"), SearchOption.TopDirectoryOnly))
            yield return file;
    }

    private static IEnumerable<string> SafeFiles(string dir, SearchOption option)
    {
        if (!Directory.Exists(dir)) return [];
        try { return Directory.EnumerateFiles(dir, "*.exe", option); }
        catch { return []; }
    }

    private static bool IsJunkExe(string fileName)
    {
        var low = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        return JunkExeBits.Any(bit => low.Contains(bit));
    }

    private static List<Game> Dedupe(List<Game> games)
    {
        var byPath = new Dictionary<string, Game>(StringComparer.Ordinal);
        foreach (var game in games)
        {
            var key = Norm(game.InstallPath);
            if (string.IsNullOrEmpty(key)) key = "name:" + Norm(game.Name);
            if (!byPath.TryGetValue(key, out var current) || Rank(game) < Rank(current))
                byPath[key] = game;
        }
        var byName = new Dictionary<string, Game>(StringComparer.Ordinal);
        foreach (var game in byPath.Values)
        {
            var key = Norm(game.Name);
            if (!byName.TryGetValue(key, out var current) || Rank(game) < Rank(current))
                byName[key] = game;
        }
        return byName.Values.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static int Rank(Game game) => SourceRank.GetValueOrDefault(game.Source, 9);

    private static string Norm(string? value)
    {
        var s = (value ?? "").Trim().ToLowerInvariant().Replace('\\', '/');
        while (s.EndsWith('/')) s = s[..^1];
        return s;
    }

    private static bool ShouldSkip(string name)
    {
        var lowered = name.Trim();
        if (SkipExact.Contains(lowered)) return true;
        var low = lowered.ToLowerInvariant();
        return SkipBits.Any(bit => low.Contains(bit));
    }

    private static bool LooksLikeGame(string name, string publisher, string install)
    {
        if (ShouldSkip(name)) return false;
        if (PublisherIsStudio(publisher)) return true;
        var path = (install ?? "").ToLowerInvariant();
        return path.Contains(@"steamapps\common")
            || path.Contains("epic games")
            || path.Contains("gog galaxy")
            || path.Contains("gog games")
            || path.Contains("xboxgames")
            || path.Contains(@"ubisoft game launcher\games")
            || path.Contains("origin games")
            || path.Contains("ea games")
            || path.Contains("battle.net");
    }

    private static bool PublisherIsStudio(string publisher)
    {
        var pub = (publisher ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(pub)) return false;
        if (pub is "ea" or "2k" or "sega" || pub.StartsWith("ea ")) return true;
        return Publishers.Any(known => known.Length > 3 && pub.Contains(known));
    }

    private static IEnumerable<(RegistryHive Hive, string Path, RegistryView View)> GogKeySpecs()
    {
        const string path = @"SOFTWARE\WOW6432Node\GOG.com\Games";
        const string alt = @"SOFTWARE\GOG.com\Games";
        yield return (RegistryHive.LocalMachine, path, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, alt, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, path, RegistryView.Registry32);
        yield return (RegistryHive.CurrentUser, path, RegistryView.Default);
    }

    private static IEnumerable<(RegistryHive Hive, string Path, RegistryView View)> UninstallKeySpecs()
    {
        const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        const string wow = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        yield return (RegistryHive.LocalMachine, path, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, wow, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, path, RegistryView.Registry32);
        yield return (RegistryHive.CurrentUser, path, RegistryView.Default);
    }

    private static RegistryKey? Open(RegistryHive hive, string path, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            return baseKey.OpenSubKey(path);
        }
        catch
        {
            return null;
        }
    }

    private static string? Read(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name)?.ToString()?.Trim();
        }
        catch
        {
            return null;
        }
    }
}
