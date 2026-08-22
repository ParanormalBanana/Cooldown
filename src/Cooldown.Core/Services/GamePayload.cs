using System.Diagnostics;

namespace Cooldown;

/// <summary>
/// Game is installed if a real game exe is on disk. Uninstall only removes that
/// exe and large libraries; sidecars (ReShade, ENB, DLSS, mods folders) stay.
/// </summary>
internal static class GamePayload
{
    public const long LargeFileBytes = 8L * 1024 * 1024;

    private static readonly string[] JunkExeBits =
    [
        "uninstall", "unins000", "unitycrash", "crashhandler", "crashreport",
        "vcredist", "dxsetup", "redist", "easyanticheat", "eosbootstrapper",
        "overlay", "cefsharp", "webhelper",
    ];

    private static readonly string[] SidecarDirs =
    [
        "mods", "mod", "reshade", "reshade-shaders", "reshade-presets",
        "enbseries", "enb", "sweetfx",
    ];

    private static readonly string[] KeepNameBits =
    [
        "reshade", "enbseries", "enblocal", "sweetfx",
        "nvngx", "dlss", "fidelityfx", "ffx_", "xess", "xell", "streamline",
    ];

    private static readonly string[] KeepExactFiles =
    [
        "dxgi.dll", "dxgi.ini", "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll",
        "opengl32.dll", "dinput8.dll", "version.dll", "winmm.dll",
        "reshade.ini", "enblocal.ini", "enbseries.ini",
    ];

    public static bool HasGameExe(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return false;
        return CandidateExes(installPath).Any(file => !IsJunkExe(Path.GetFileName(file)));
    }

    public static bool IsGameRunning(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            return false;
        string root;
        try { root = Path.GetFullPath(installPath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar; }
        catch { return false; }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in CandidateExes(installPath))
        {
            if (IsJunkExe(Path.GetFileName(file))) continue;
            names.Add(Path.GetFileNameWithoutExtension(file));
        }
        if (names.Count == 0) return false;

        foreach (var name in names)
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    var exe = proc.MainModule?.FileName;
                    if (string.IsNullOrEmpty(exe))
                        return true;
                    var full = Path.GetFullPath(exe);
                    if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                    return true;
                }
                finally { proc.Dispose(); }
            }
        }
        return false;
    }

    public static bool IsSidecarDir(string dirName) =>
        SidecarDirs.Any(name => dirName.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static bool ShouldDelete(string file)
    {
        if (ShouldKeep(file)) return false;
        var name = Path.GetFileName(file);
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !IsJunkExe(name))
            return true;
        try
        {
            return new FileInfo(file).Length >= LargeFileBytes;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldKeep(string file)
    {
        var name = Path.GetFileName(file);
        if (KeepExactFiles.Any(keep => name.Equals(keep, StringComparison.OrdinalIgnoreCase)))
            return true;
        var low = Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
        return KeepNameBits.Any(bit => low.Contains(bit));
    }

    private static IEnumerable<string> CandidateExes(string root)
    {
        foreach (var file in SafeExes(root))
            yield return file;
        foreach (var sub in new[] { "bin", "Bin", "Binaries", "Win64", "x64", "Game", "Content" })
        {
            foreach (var file in SafeExes(Path.Combine(root, sub)))
                yield return file;
        }
        foreach (var file in SafeExes(Path.Combine(root, "Binaries", "Win64")))
            yield return file;
    }

    private static IEnumerable<string> SafeExes(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        try { return Directory.EnumerateFiles(dir, "*.exe", SearchOption.TopDirectoryOnly); }
        catch { return []; }
    }

    private static bool IsJunkExe(string fileName)
    {
        var low = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        return JunkExeBits.Any(bit => low.Contains(bit));
    }
}
