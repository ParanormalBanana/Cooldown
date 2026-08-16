namespace Cooldown.ViewModels;

internal sealed class GameRow
{
    public GameRow(IReadOnlyList<GameItem> items, string? header = null)
    {
        Items = items;
        Header = header ?? "";
    }

    public IReadOnlyList<GameItem> Items { get; }
    public string Header { get; }
    public bool IsHeader => Header.Length > 0;
    public bool HasItems => Items.Count > 0;
}

internal sealed class RewardItem
{
    public RewardItem(string message, string when, string points)
    {
        Message = message;
        When = when;
        Points = points;
    }

    public string Message { get; }
    public string When { get; }
    public string Points { get; }
}
