namespace Cooldown;

internal static class Theme
{
    public const string Classic1998 = "1998";
    public const string Xp2001 = "2001";
    public const string Win112021 = "2021";

    public static string Normalize(string? id)
    {
        if (string.Equals(id, Xp2001, StringComparison.OrdinalIgnoreCase)) return Xp2001;
        if (string.Equals(id, Win112021, StringComparison.OrdinalIgnoreCase)) return Win112021;
        return Classic1998;
    }

    public static bool IsXp(string? id) => Normalize(id) == Xp2001;
    public static bool IsWin11(string? id) => Normalize(id) == Win112021;
}
