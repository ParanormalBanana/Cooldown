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

internal sealed class WatchFolderItem : ObservableObject
{
    private readonly MainViewModel _vm;
    private bool _enabled;

    public WatchFolderItem(MainViewModel vm, string key, string label, bool custom, bool enabled)
    {
        _vm = vm;
        Key = key;
        Label = label;
        IsCustom = custom;
        _enabled = enabled;
    }

    public string Key { get; }
    public string Label { get; }
    public bool IsCustom { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value))
                _vm.SetWatchEnabled(this, value);
        }
    }
}

internal sealed class BlacklistItem
{
    public BlacklistItem(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }
}
