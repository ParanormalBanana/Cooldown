using System.Windows.Media;
using Cooldown.Models;

namespace Cooldown.ViewModels;

internal sealed class GameItem : ObservableObject
{
    private ImageSource? _cover;
    private bool _onCooldown;
    private int _loadedWidth;

    public GameItem(Game game, bool onCooldown, bool hidden, CooldownEntry? entry = null, GameStats? stats = null)
    {
        Game = game;
        IsHidden = hidden;
        _onCooldown = onCooldown;
        Launcher = game.Source;
        DaysText = "";
        if (onCooldown)
            DaysText = $"{Rewards.DaysOnCooldown(entry?.CreatedAt ?? "")} day(s) on cooldown";
        else if (stats is not null)
            DaysText = $"{stats.LastCooldownDays} day(s) on cooldown";
        ReinstallsText = stats is null ? "" : $"{stats.Reinstalls} reinstalls";
        ScoreText = stats is null ? "" : $"{stats.Points} accumulated points";
    }

    public Game Game { get; }
    public string Id => Game.Id;
    public string Name => Game.Name;
    public bool IsHidden { get; }
    public string Title => Game.Name;
    public string Launcher { get; }
    public string DaysText { get; }
    public string ReinstallsText { get; }
    public string ScoreText { get; }

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
        if (Cover is not null && decodeWidth <= _loadedWidth) return;
        var cached = Covers.TryGetCached(Game, decodeWidth);
        if (cached is not null)
        {
            Cover = cached;
            _loadedWidth = decodeWidth;
            return;
        }
        var image = await Covers.EnsureAsync(Game, decodeWidth);
        if (image is not null && decodeWidth >= _loadedWidth)
        {
            Cover = image;
            _loadedWidth = decodeWidth;
        }
    }
}
