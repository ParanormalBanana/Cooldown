namespace Cooldown;

internal static class Theme
{
    public const string Classic1998 = "1998";
    public const string Xp2001 = "2001";

    public static string Normalize(string? id) =>
        string.Equals(id, Xp2001, StringComparison.OrdinalIgnoreCase) ? Xp2001 : Classic1998;

    public static bool IsXp(string? id) => Normalize(id) == Xp2001;
}
