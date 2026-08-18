using System.Text;
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
        "ea app", "origin", "battle.net", "xbox", "xbox app", "riot client",
        "rockstar games launcher", "rockstar games social club",
    };

    private static readonly Dictionary<string, int> SourceRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Steam"] = 0, ["Epic"] = 1, ["GOG"] = 2, ["Ubisoft"] = 3, ["EA"] = 4,
        ["Battle.net"] = 5, ["Xbox"] = 6, ["Rockstar"] = 7, ["Riot"] = 8,
        ["Windows"] = 9, ["Custom"] = 10,
    };

    public static readonly string[] WatchSources =
    [
        "Steam", "Epic", "GOG", "Ubisoft", "EA", "Battle.net", "Xbox", "Rockstar", "Riot", "Windows",
    ];

    public static List<Game> Discover(IEnumerable<string>? disabledSources = null)
    {
        var off = disabledSources?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var found = new List<Game>();
        void Add(string source, Func<List<Game>> scan)
        {
            if (off.Contains(source)) return;
            if (source != "Windows" && !SourceAvailable(source)) return;
            try { found.AddRange(scan()); }
            catch (Exception ex) { Log.Warn($"{source} scan skipped: {ex.Message}"); }
        }
        Add("Steam", SteamGames);
        Add("Epic", EpicGames);
        Add("GOG", GogGames);
        Add("Ubisoft", UbisoftGames);
        Add("EA", EaGames);
        Add("Battle.net", BattleNetGames);
        Add("Xbox", XboxGames);
        Add("Rockstar", RockstarGames);
        Add("Riot", RiotGames);
        Add("Windows", RegistryGames);
        return Combine(found);
    }

    public static string WatchLabel(string source) => source == "Windows" ? "Other" : source;

    public static bool SourceAvailable(string source) => source switch
    {
        "Steam" => SteamRoot() is not null,
        "Epic" => Directory.Exists(EpicManifestsDir()),
        "GOG" => HasRegistryKey(GogKeySpecs()),
        "Ubisoft" => HasRegistryKey(UbisoftKeySpecs()),
        "EA" => HasRegistryKey(EaKeySpecs()) || EaInstallRoots().Any(),
        "Battle.net" => File.Exists(BattleNetProductDb())
                        || Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net")),
        "Xbox" => XboxPresent(),
        "Rockstar" => RockstarLauncherPresent(),
        "Riot" => Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games")),
        "Windows" => false,
        _ => false,
    };

    public static List<Game> Combine(params IEnumerable<Game>[] batches)
    {
        var found = new List<Game>();
        foreach (var batch in batches)
            found.AddRange(batch);
        return Dedupe(found);
    }

    public static Game FromFolder(string path, string? name = null, string source = "Custom", string? id = null)
    {
        var full = Path.GetFullPath(path);
        var folderName = string.IsNullOrWhiteSpace(name) ? new DirectoryInfo(full).Name : name.Trim();
        return new Game
        {
            Id = id ?? CustomId(full),
            Name = folderName,
            Source = source,
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
        var path = game.InstallPath;
        if (scanned is not null)
        {
            var match = scanned.FirstOrDefault(item => SameGame(item, game, names: false));
            if (!string.IsNullOrWhiteSpace(match?.InstallPath))
                path = match.InstallPath;
        }
        return GamePayload.HasGameExe(path);
    }

    /// <summary>
    /// Steam, Windows leftovers, and custom copies of the same title.
    /// </summary>
    public static bool SameGame(Game a, Game b, bool names = true)
    {
        if (string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase))
            return true;
        var steamA = SteamAppIdFor(a);
        var steamB = SteamAppIdFor(b);
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

    public static string? SteamAppIdFor(Game game)
    {
        var direct = SteamAppKey(game);
        if (!string.IsNullOrEmpty(direct)) return direct;
        return SteamAppIdFromInstallPath(game.InstallPath);
    }

    public static string? SteamAppIdFromInstallPath(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return null;
        string full;
        try { full = Path.GetFullPath(installPath); }
        catch { return null; }
        var steamapps = FindSteamapps(full);
        if (steamapps is null) return null;
        var folder = new DirectoryInfo(full.TrimEnd('\\', '/')).Name;
        string[] manifests;
        try { manifests = Directory.GetFiles(steamapps, "appmanifest_*.acf"); }
        catch { return null; }
        foreach (var manifest in manifests)
        {
            try
            {
                var data = Vdf.Parse(File.ReadAllText(manifest));
                var state = Vdf.Child(data, "AppState") ?? data;
                var installDir = Vdf.Get(state, "installdir").Trim();
                if (!installDir.Equals(folder, StringComparison.OrdinalIgnoreCase)) continue;
                var appId = Vdf.Get(state, "appid");
                if (appId.Length > 0 && appId.All(char.IsDigit)) return appId;
            }
            catch { /* skip bad manifest */ }
        }
        return null;
    }

    private static string? FindSteamapps(string path)
    {
        var current = new DirectoryInfo(path);
        for (var i = 0; i < 8 && current is not null; i++)
        {
            if (current.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                return current.FullName;
            if (current.Parent?.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) == true
                && current.Name.Equals("common", StringComparison.OrdinalIgnoreCase))
                return current.Parent.FullName;
            current = current.Parent;
        }
        return null;
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
        var manifests = EpicManifestsDir();
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

    private static List<Game> UbisoftGames()
    {
        var games = new List<Game>();
        foreach (var (hive, path, view) in UbisoftKeySpecs())
        {
            using var root = Open(hive, path, view);
            if (root is null) continue;
            foreach (var id in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(id);
                if (sub is null) continue;
                var dir = Read(sub, "InstallDir");
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                var name = new DirectoryInfo(dir.TrimEnd('\\', '/')).Name;
                if (string.IsNullOrEmpty(name) || ShouldSkip(name)) continue;
                games.Add(new Game
                {
                    Id = $"ubisoft:{id}",
                    Name = name,
                    Source = "Ubisoft",
                    InstallPath = dir,
                    Publisher = "Ubisoft",
                });
            }
        }
        return games;
    }

    private static List<Game> EaGames()
    {
        var games = new List<Game>();
        foreach (var (hive, path, view) in EaKeySpecs())
        {
            using var root = Open(hive, path, view);
            if (root is null) continue;
            foreach (var id in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(id);
                if (sub is null) continue;
                var name = Read(sub, "DisplayName") ?? Read(sub, "ProductName") ?? id;
                var dir = Read(sub, "InstallDir") ?? Read(sub, "InstallLocation") ?? Read(sub, "DisplayIcon");
                if (!string.IsNullOrEmpty(dir) && File.Exists(dir))
                    dir = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(name) || ShouldSkip(name)) continue;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                games.Add(new Game
                {
                    Id = $"ea:{id}",
                    Name = name,
                    Source = "EA",
                    InstallPath = dir,
                    Publisher = "Electronic Arts",
                });
            }
        }
        foreach (var root in EaInstallRoots())
        {
            foreach (var folder in GameFoldersIn(root))
            {
                games.Add(FromFolder(folder, source: "EA", id: "ea:" + Norm(folder)));
            }
        }
        return games;
    }

    private static string EpicManifestsDir() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

    private static string BattleNetProductDb() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Battle.net", "Agent", "product.db");

    private static List<Game> BattleNetGames()
    {
        var games = new List<Game>();
        var db = BattleNetProductDb();
        if (!File.Exists(db)) return games;
        byte[] data;
        try { data = File.ReadAllBytes(db); }
        catch { return games; }
        foreach (var dir in ExistingDirsIn(data))
        {
            if (IsBattleNetNoise(dir) || IsGenericRoot(dir) || ShouldSkip(Path.GetFileName(dir))) continue;
            if (!LooksLikeGameFolder(dir)) continue;
            games.Add(FromFolder(dir, source: "Battle.net", id: "battlenet:" + Norm(dir)));
        }
        return games;
    }

    private static List<Game> RockstarGames()
    {
        var games = new List<Game>();
        foreach (var (hive, path, view) in RockstarKeySpecs())
        {
            using var root = Open(hive, path, view);
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                if (name.Equals("Launcher", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Social Club", StringComparison.OrdinalIgnoreCase))
                    continue;
                using var sub = root.OpenSubKey(name);
                if (sub is null) continue;
                var dir = Read(sub, "InstallFolder") ?? Read(sub, "InstallDir") ?? Read(sub, "Install Folder");
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir) || ShouldSkip(name)) continue;
                games.Add(new Game
                {
                    Id = "rockstar:" + Norm(name),
                    Name = name,
                    Source = "Rockstar",
                    InstallPath = dir,
                    Publisher = "Rockstar Games",
                });
            }
        }
        var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rockstar Games");
        foreach (var folder in GameFoldersIn(pf))
        {
            var name = new DirectoryInfo(folder).Name;
            if (name.Equals("Launcher", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Social Club", StringComparison.OrdinalIgnoreCase))
                continue;
            games.Add(FromFolder(folder, name, "Rockstar", "rockstar:" + Norm(folder)));
        }
        return games;
    }

    private static List<Game> RiotGames()
    {
        var games = new List<Game>();
        var meta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Riot Games", "Metadata");
        if (Directory.Exists(meta))
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(meta, "*.product_settings.yaml", SearchOption.AllDirectories); }
            catch { files = []; }
            foreach (var file in files)
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch { continue; }
                var dir = YamlString(text, "product_install_full_path");
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                var product = Path.GetFileName(Path.GetDirectoryName(file)) ?? "";
                if (product.Contains("riot_client", StringComparison.OrdinalIgnoreCase)
                    || product.Contains("vanguard", StringComparison.OrdinalIgnoreCase))
                    continue;
                var name = RiotDisplayName(product);
                if (ShouldSkip(name)) continue;
                games.Add(new Game
                {
                    Id = "riot:" + Norm(product),
                    Name = name,
                    Source = "Riot",
                    InstallPath = dir,
                    Publisher = "Riot Games",
                });
            }
        }
        foreach (var (hive, path, view) in UninstallKeySpecs())
        {
            using var root = Open(hive, path, view);
            if (root is null) continue;
            foreach (var subName in root.GetSubKeyNames())
            {
                if (!subName.StartsWith("Riot Game ", StringComparison.OrdinalIgnoreCase)) continue;
                if (subName.Contains("Riot_Client", StringComparison.OrdinalIgnoreCase)) continue;
                using var sub = root.OpenSubKey(subName);
                if (sub is null) continue;
                var name = Read(sub, "DisplayName");
                var dir = Read(sub, "InstallLocation");
                if (string.IsNullOrEmpty(name) || ShouldSkip(name)) continue;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                games.Add(new Game
                {
                    Id = "riot:" + Norm(subName),
                    Name = name,
                    Source = "Riot",
                    InstallPath = dir,
                    Publisher = "Riot Games",
                    UninstallString = Read(sub, "UninstallString") ?? "",
                });
            }
        }
        return games;
    }

    private static string RiotDisplayName(string product)
    {
        var id = product.Replace(".live", "", StringComparison.OrdinalIgnoreCase)
            .Replace(".pbe", "", StringComparison.OrdinalIgnoreCase);
        return id.ToLowerInvariant() switch
        {
            "league_of_legends" => "League of Legends",
            "valorant" => "VALORANT",
            "bacon" => "Legends of Runeterra",
            "wildrift" => "League of Legends: Wild Rift",
            "lion" or "lions" => "2XKO",
            _ => id.Replace('_', ' '),
        };
    }

    private static string? YamlString(string text, string key)
    {
        var needle = key + ":";
        var at = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var rest = text[(at + needle.Length)..].TrimStart();
        if (rest.StartsWith('"'))
        {
            var end = rest.IndexOf('"', 1);
            if (end < 0) return null;
            return rest[1..end].Replace('/', '\\');
        }
        var line = rest.Split(['\r', '\n'], 2)[0].Trim().Trim('"');
        return string.IsNullOrEmpty(line) ? null : line.Replace('/', '\\');
    }

    private static List<Game> XboxGames()
    {
        var games = new List<Game>();
        foreach (var root in XboxInstallRoots())
        {
            foreach (var folder in GameFoldersIn(root))
            {
                var name = new DirectoryInfo(folder).Name;
                if (name.Equals("Content", StringComparison.OrdinalIgnoreCase) && folder.TrimEnd('\\', '/').Contains('\\'))
                    name = Directory.GetParent(folder)?.Name ?? name;
                games.Add(FromFolder(folder, name, "Xbox", "xbox:" + Norm(folder)));
            }
        }
        return games;
    }

    private static IEnumerable<string> GameFoldersIn(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;
        IEnumerable<string> children;
        try { children = Directory.GetDirectories(root); }
        catch { yield break; }
        foreach (var child in children)
        {
            if (LooksLikeGameFolder(child))
            {
                yield return child;
                continue;
            }
            IEnumerable<string> nested;
            try { nested = Directory.GetDirectories(child); }
            catch { continue; }
            foreach (var inner in nested)
            {
                if (LooksLikeGameFolder(inner))
                    yield return inner;
            }
        }
    }

    private static IEnumerable<string> EaInstallRoots()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (var root in new[]
                 {
                     Path.Combine(pf, "EA Games"),
                     Path.Combine(pf86, "EA Games"),
                     Path.Combine(pf, "Origin Games"),
                     Path.Combine(pf86, "Origin Games"),
                 })
        {
            if (Directory.Exists(root)) yield return root;
        }
        foreach (var (hive, path, view) in new[]
                 {
                     (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\EA Games", RegistryView.Registry64),
                     (RegistryHive.LocalMachine, @"SOFTWARE\EA Games", RegistryView.Registry64),
                     (RegistryHive.LocalMachine, @"SOFTWARE\EA Games", RegistryView.Registry32),
                 })
        {
            using var key = Open(hive, path, view);
            if (key is null) continue;
            var dir = Read(key, "Install Dir") ?? Read(key, "InstallDir");
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) yield return dir;
        }
    }

    private static bool XboxPresent() => Directory.Exists(@"C:\XboxGames") || HasRegistryKey(XboxKeySpecs());

    private static IEnumerable<(RegistryHive Hive, string Path, RegistryView View)> XboxKeySpecs()
    {
        yield return (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\GamingServices", RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\GamingServices", RegistryView.Registry32);
        yield return (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Xbox", RegistryView.Registry64);
    }

    private static IEnumerable<string> XboxInstallRoots()
    {
        yield return @"C:\XboxGames";
    }

    private static IEnumerable<string> ExistingDirsIn(byte[] data)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = 0;
        for (var i = 0; i <= data.Length; i++)
        {
            if (i < data.Length && data[i] is >= 32 and < 127)
                continue;
            var len = i - start;
            if (len >= 4)
            {
                var raw = Encoding.ASCII.GetString(data, start, len).Replace('/', '\\').TrimEnd('\\');
                if (raw.Length >= 4 && char.IsAsciiLetter(raw[0]) && raw[1] == ':' && raw[2] == '\\'
                    && raw.IndexOfAny(Path.GetInvalidPathChars()) < 0
                    && !IsGenericRoot(raw) && !IsBattleNetNoise(raw))
                {
                    string? full = null;
                    try
                    {
                        if (Directory.Exists(raw))
                            full = Path.GetFullPath(raw).TrimEnd('\\', '/');
                    }
                    catch { /* ignore */ }
                    if (full is not null && seen.Add(full))
                        yield return full;
                }
            }
            start = i + 1;
        }
    }

    private static bool IsGenericRoot(string dir)
    {
        string resolved;
        try { resolved = Path.GetFullPath(dir).TrimEnd('\\', '/').ToLowerInvariant(); }
        catch { return true; }
        if (resolved.Length < 4) return true;
        if (resolved.EndsWith(@"\program files") || resolved.EndsWith(@"\program files (x86)")
            || resolved.EndsWith(@"\programdata") || resolved.EndsWith(@"\windows")
            || resolved.EndsWith(@"\users"))
            return true;
        return resolved.Split('\\', '/').Length < 3;
    }

    private static bool IsBattleNetNoise(string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
        if (name.Equals("Agent", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Equals("Battle.net", StringComparison.OrdinalIgnoreCase)) return true;
        var low = dir.Replace('/', '\\').ToLowerInvariant();
        return low.Contains(@"\battle.net\agent");
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
                    if (OwnedByOtherStore(publisher, install)) continue;
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
        return GamePayload.HasGameExe(path);
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

    private static int Rank(Game game) => SourceRank.GetValueOrDefault(game.Source, 11);

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

    private static bool OwnedByOtherStore(string publisher, string install)
    {
        var path = (install ?? "").ToLowerInvariant();
        if (path.Contains(@"steamapps\common") || path.Contains("epic games")
            || path.Contains("gog galaxy") || path.Contains("gog games")
            || path.Contains("xboxgames") || path.Contains("windowsapps")
            || path.Contains("ubisoft") || path.Contains("origin games")
            || path.Contains(@"\ea games") || path.Contains("battle.net")
            || path.Contains("rockstar games") || path.Contains("riot games"))
            return true;
        var pub = (publisher ?? "").ToLowerInvariant();
        return pub.Contains("ubisoft")
            || pub.Contains("electronic arts")
            || pub.Contains("blizzard")
            || pub.Contains("rockstar")
            || pub.Contains("riot games")
            || pub is "ea" || pub.StartsWith("ea ");
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

    private static IEnumerable<(RegistryHive Hive, string Path, RegistryView View)> UbisoftKeySpecs()
    {
        const string path = @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs";
        const string alt = @"SOFTWARE\Ubisoft\Launcher\Installs";
        yield return (RegistryHive.LocalMachine, path, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, alt, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, alt, RegistryView.Registry32);
    }

    private static IEnumerable<(RegistryHive Hive, string Path, RegistryView View)> EaKeySpecs()
    {
        const string origin = @"SOFTWARE\WOW6432Node\Origin Games";
        const string originAlt = @"SOFTWARE\Origin Games";
        const string ea = @"SOFTWARE\WOW6432Node\EA Games";
        yield return (RegistryHive.LocalMachine, origin, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, originAlt, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, originAlt, RegistryView.Registry32);
        yield return (RegistryHive.LocalMachine, ea, RegistryView.Registry64);
        yield return (RegistryHive.CurrentUser, origin, RegistryView.Default);
    }

    private static IEnumerable<(RegistryHive Hive, string Path, RegistryView View)> RockstarKeySpecs()
    {
        const string path = @"SOFTWARE\WOW6432Node\Rockstar Games";
        const string alt = @"SOFTWARE\Rockstar Games";
        yield return (RegistryHive.LocalMachine, path, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, alt, RegistryView.Registry64);
        yield return (RegistryHive.LocalMachine, alt, RegistryView.Registry32);
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

    private static bool HasRegistryKey(IEnumerable<(RegistryHive Hive, string Path, RegistryView View)> specs)
    {
        foreach (var spec in specs)
        {
            using var key = Open(spec.Hive, spec.Path, spec.View);
            if (key is not null) return true;
        }
        return false;
    }

    private static bool RockstarLauncherPresent()
    {
        if (HasRegistryKey(RockstarKeySpecs())) return true;
        var launcher = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Rockstar Games", "Launcher");
        return Directory.Exists(launcher);
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
