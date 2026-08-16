using System.Windows.Media;
using Cooldown.Models;

namespace Cooldown.ViewModels;

internal sealed class GameItem : ObservableObject
{
    private ImageSource? _cover;
    private bool _onCooldown;
    private bool _requested;

    public GameItem(Game game, bool installed, bool onCooldown, bool hidden)
    {
        Game = game;
        IsInstalled = installed;
        IsHidden = hidden;
        _onCooldown = onCooldown;
    }

    public Game Game { get; }
    public string Id => Game.Id;
    public string Name => Game.Name;
    public bool IsInstalled { get; }
    public bool IsHidden { get; }
    public string Title => IsInstalled ? Game.Name : $"{Game.Name} (missing)";

    public bool OnCooldown
    {
        get => _onCooldown;
        set => Set(ref _onCooldown, value);
    }

    public ImageSource? Cover
    {
        get => _cover;
        private set => Set(ref _cover, value);
    }

    public async void EnsureCover(int decodeWidth)
    {
        if (_requested && Cover is not null) return;
        var cached = Covers.TryGetCached(Game, decodeWidth);
        if (cached is not null)
        {
            Cover = cached;
            _requested = true;
            return;
        }
        if (_requested) return;
        _requested = true;
        var image = await Covers.EnsureAsync(Game, decodeWidth);
        if (image is not null) Cover = image;
    }

    public void ResetCoverRequest() => _requested = false;
}
