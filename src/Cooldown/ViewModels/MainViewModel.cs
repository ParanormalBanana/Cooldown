using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Cooldown.Models;

namespace Cooldown.ViewModels;

internal sealed class MainViewModel : ObservableObject
{
    private AppState _state;
    private List<Game> _games;
    private string _search = "";
    private string _rescanLabel = "Rescan";
    private bool _scanning;
    private bool _showSplash;
    private string _splashStatus = "Peeking at Steam";
    private string _toast = "";
    private bool _toastVisible;
    private int _columnCount = 4;
    private double _cardWidth = 220;
    private double _coverHeight = 103;
    private string _modal = "none";
    private GameItem? _selected;
    private CooldownEntry? _active;
    private bool _showHidden;
    private string _emptyMessage = "Nothing matches that search.";
    private bool _showEmpty;
    private readonly DispatcherTimer _searchTimer;
    private readonly DispatcherTimer _splashTimer;
    private readonly DispatcherTimer _toastTimer;
    private int _splashTick;
    private bool _scanBusy;
    private int _scanGen;
    private Dispatcher? _ui;
    private bool _suspendWatch;

    private static readonly string[] SplashMessages =
    [
        "Peeking at Steam", "Sniffing around Epic", "Checking GOG",
        "Poking Ubisoft", "Asking EA", "Waking Battle.net",
        "Checking Rockstar", "Ping Riot",
        "Rummaging in the registry", "Sorting the loot",
    ];

    public MainViewModel()
    {
        _state = Storage.Load();
        _games = _state.KnownGames.Where(g => !IsIgnored(g)).ToList();
        _state.Cooldowns = _state.Cooldowns.Where(c => !IsIgnored(c.Game)).ToList();
        _showSplash = _games.Count == 0 && _state.Cooldowns.Count == 0;
        Rows = new ObservableCollection<GameRow>();
        WatchFolders = new ObservableCollection<WatchFolderItem>();
        Blacklist = new ObservableCollection<BlacklistItem>();

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RebuildRows(); };

        _splashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
        _splashTimer.Tick += (_, _) => AnimateSplash();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastVisible = false; };

        OpenProgressCommand = new RelayCommand(_ => OpenProgressPage());
        OpenLibraryCommand = new RelayCommand(_ => CloseModal());
        RescanCommand = new RelayCommand(_ => Rescan(), _ => !Scanning);
        CloseModalCommand = new RelayCommand(_ => CloseModal());
        PutOnCooldownCommand = new RelayCommand(_ => PutOnCooldown());
        TakeOffCooldownCommand = new RelayCommand(_ => TakeOffCooldown());
        OpenAddCommand = new RelayCommand(_ => ScanCustomFolder());
        ToggleHiddenCommand = new RelayCommand(_ => ToggleHidden());
        ToggleCooldownsOnTopCommand = new RelayCommand(_ => ToggleCooldownsOnTop());
        OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
        RemoveWatchFolderCommand = new RelayCommand(p =>
        {
            if (p is WatchFolderItem item) RemoveWatchFolder(item);
        });
        RemoveBlacklistCommand = new RelayCommand(p =>
        {
            if (p is BlacklistItem item) RemoveFromBlacklist(item);
        });
        SelectGameCommand = new RelayCommand(p =>
        {
            if (p is GameItem item) SelectGame(item);
        });
    }

    public ObservableCollection<GameRow> Rows { get; }
    public ObservableCollection<WatchFolderItem> WatchFolders { get; }
    public ObservableCollection<BlacklistItem> Blacklist { get; }
    public ICommand OpenProgressCommand { get; }
    public ICommand OpenLibraryCommand { get; }
    public ICommand RescanCommand { get; }
    public ICommand CloseModalCommand { get; }
    public ICommand PutOnCooldownCommand { get; }
    public ICommand TakeOffCooldownCommand { get; }
    public ICommand OpenAddCommand { get; }
    public ICommand ToggleHiddenCommand { get; }
    public ICommand ToggleCooldownsOnTopCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand RemoveWatchFolderCommand { get; }
    public ICommand RemoveBlacklistCommand { get; }
    public ICommand SelectGameCommand { get; }

    public string Search { get => _search; set { if (Set(ref _search, value)) DebounceSearch(); } }
    public string RescanLabel { get => _rescanLabel; set => Set(ref _rescanLabel, value); }
    public bool Scanning { get => _scanning; set { if (Set(ref _scanning, value)) Raise(nameof(RescanEnabled)); } }
    public bool RescanEnabled => !Scanning && ParentEnabled;
    public bool ParentEnabled => _modal == "none";
    public bool ShowSplash { get => _showSplash; set => Set(ref _showSplash, value); }
    public string SplashStatus { get => _splashStatus; set => Set(ref _splashStatus, value); }
    public string Toast { get => _toast; set => Set(ref _toast, value); }
    public bool ToastVisible { get => _toastVisible; set => Set(ref _toastVisible, value); }
    public int ColumnCount { get => _columnCount; private set => Set(ref _columnCount, value); }
    public double CardWidth { get => _cardWidth; private set => Set(ref _cardWidth, value); }
    public double CoverHeight { get => _coverHeight; private set => Set(ref _coverHeight, value); }
    public string RankText => Rewards.Rank(_state) == "Unranked" ? "Unranked" : $"Rank {Rewards.Rank(_state)}";
    public string RankLetter => Rewards.Rank(_state);
    public bool IsRanked => _state.HasRanked;
    public string StreakText => $"{Rewards.CurrentStreak(_state)} days streak  ·  Best {_state.BestStreakDays}";
    public string ScoreText => $"{_state.Points}";
    public string CurrentStreakText => $"{Rewards.CurrentStreak(_state)} days";
    public string BestStreakText => $"{_state.BestStreakDays} days";

    public bool ShowHidden { get => _showHidden; private set => Set(ref _showHidden, value); }
    public bool CooldownsOnTop
    {
        get => _state.CooldownsOnTop;
        private set
        {
            if (_state.CooldownsOnTop == value) return;
            _state.CooldownsOnTop = value;
            Raise(nameof(CooldownsOnTop));
        }
    }
    public string EmptyMessage { get => _emptyMessage; private set => Set(ref _emptyMessage, value); }
    public bool ShowEmpty { get => _showEmpty; private set => Set(ref _showEmpty, value); }

    public bool ModalOpen => _modal != "none";
    public bool ModalPutOn => _modal == "put";
    public bool ModalTakeOff => _modal == "off";
    public bool ModalProgress => _modal == "progress";
    public bool ModalSettings => _modal == "settings";
    public string ModalTitle => _modal switch
    {
        "progress" => "My determination",
        "settings" => "Settings",
        "put" or "off" => string.IsNullOrWhiteSpace(SelectedName) ? "Cooldown" : SelectedName,
        _ => "Cooldown",
    };
    public bool HasBlacklist => Blacklist.Count > 0;
    public string SelectedName => _selected?.Name ?? _active?.Game.Name ?? "";
    public GameItem? SelectedGame => _selected;
    public string PutOnPrompt => $"You're about to put {SelectedName} on cooldown.";
    public string TakeOffPrompt => $"{SelectedName} is on cooldown.";

    public void Start(Dispatcher ui)
    {
        _ui = ui;
        var empty = _games.Count == 0 && _state.Cooldowns.Count == 0;
        ShowSplash = empty;
        RebuildRows();
        RaiseRank();
        if (!empty) PrefetchVisibleCovers();
        Rescan(silent: !empty);
        Task.Run(() =>
        {
            try { Scheduler.EnsureBackgroundTasks(_state); }
            catch (Exception ex) { Log.Error("Could not sync background tasks", ex); }
        });
    }

    public void SetViewportWidth(double width)
    {
        if (width < 200) return;
        var inner = Math.Max(400, width - 36);
        var cols = Math.Clamp((int)(inner / 196), 2, 6);
        var gap = 8.0;
        var cardW = Math.Max(170, (inner - gap * (cols + 1)) / cols);
        var coverH = Math.Max(90, cardW * (215.0 / 460.0));
        var changed = cols != ColumnCount || Math.Abs(cardW - CardWidth) > 12;
        ColumnCount = cols;
        CardWidth = cardW;
        CoverHeight = coverH;
        if (changed) RebuildRows();
    }

    public void OnCardRealized(GameItem item) =>
        item.EnsureCover((int)Math.Round(CardWidth));

    private void DebounceSearch()
    {
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void RebuildRows()
    {
        var listed = new List<Game>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in _games)
        {
            if (!IsIgnored(game) && seen.Add(game.Id))
                listed.Add(game);
        }
        foreach (var entry in _state.Cooldowns)
        {
            if (IsIgnored(entry.Game)) continue;
            var live = _games.FirstOrDefault(g => Detector.SameGame(g, entry.Game));
            if (live is not null)
            {
                seen.Add(live.Id);
                continue;
            }
            if (seen.Add(entry.Game.Id))
                listed.Add(entry.Game);
        }

        listed = listed
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var query = (_search ?? "").Trim();
        var hidden = _state.HiddenIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool OnCooldown(Game g) =>
            _state.Cooldowns.Any(c => Detector.SameGame(c.Game, g));
        bool Matches(Game g) =>
            string.IsNullOrEmpty(query)
            || g.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || g.Source.Contains(query, StringComparison.OrdinalIgnoreCase);

        GameItem Make(Game g) =>
            new(g, OnCooldown(g), hidden.Contains(g.Id));

        var visible = listed.Where(g => !hidden.Contains(g.Id) && Matches(g)).Select(Make).ToList();
        var hiddenItems = ShowHidden
            ? listed.Where(g => hidden.Contains(g.Id) && Matches(g)).Select(Make).ToList()
            : [];

        Rows.Clear();
        AddGroupedRows(visible);
        if (hiddenItems.Count > 0)
        {
            Rows.Add(new GameRow([], "Hidden"));
            AddGroupedRows(hiddenItems);
        }

        if (Rows.Count == 0 && !ShowSplash && listed.Count > 0)
        {
            ShowEmpty = true;
            EmptyMessage = hidden.Count > 0 && visible.Count == 0 && !ShowHidden
                ? "Hidden games are out of sight. Right-click to show them."
                : "Nothing matches that search.";
        }
        else
        {
            ShowEmpty = false;
        }
        PrefetchVisibleCovers();
    }

    private void AddGroupedRows(List<GameItem> items)
    {
        if (items.Count == 0) return;
        if (!CooldownsOnTop)
        {
            AddItemRows(items);
            return;
        }
        var cooling = items.Where(item => item.OnCooldown).ToList();
        var rest = items.Where(item => !item.OnCooldown).ToList();
        AddItemRows(cooling);
        AddItemRows(rest);
    }

    private void AddItemRows(List<GameItem> items)
    {
        var cols = Math.Max(1, ColumnCount);
        for (var i = 0; i < items.Count; i += cols)
            Rows.Add(new GameRow(items.Skip(i).Take(cols).ToList()));
    }

    private void PrefetchVisibleCovers()
    {
        var width = (int)Math.Round(CardWidth);
        foreach (var row in Rows.Take(8))
        {
            foreach (var item in row.Items)
                item.EnsureCover(width);
        }
    }

    private void Rescan(bool silent = false)
    {
        if (_scanBusy) return;
        _scanBusy = true;
        var gen = ++_scanGen;
        if (!silent)
        {
            Scanning = true;
            RescanLabel = "Scanning";
            if (_games.Count == 0 && _state.Cooldowns.Count == 0)
            {
                ShowSplash = true;
                _splashTimer.Start();
            }
        }
        Task.Run(() =>
        {
            var games = CollectGames();
            _ui?.BeginInvoke(() =>
            {
                if (gen != _scanGen) return;
                ApplyGames(games, silent);
            });
        });
    }

    private List<Game> CollectGames()
    {
        var disabledDirs = _state.DisabledScanDirs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extras = _state.CustomScanDirs
            .Where(dir => !disabledDirs.Contains(dir))
            .SelectMany(Detector.ScanDirectory);
        var custom = _state.CustomGames.Where(g => !BelongsToCustomScanDir(g.InstallPath));
        return Detector.Combine(Detector.Discover(_state.DisabledSources), extras, custom)
            .Where(g => !IsIgnored(g))
            .ToList();
    }

    private void ApplyGames(List<Game> games, bool silent = false)
    {
        var changed = !SameIds(_games, games);
        _games = games;
        _state.KnownGames = games;
        Storage.Save(_state);
        _scanBusy = false;
        Scanning = false;
        RescanLabel = "Rescan";
        ShowSplash = false;
        _splashTimer.Stop();
        if (!silent || changed)
            RebuildRows();
        RaiseRank();
    }

    private static bool SameIds(List<Game> left, List<Game> right)
    {
        if (left.Count != right.Count) return false;
        var ids = left.Select(g => g.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return right.All(g => ids.Contains(g.Id));
    }

    private void AnimateSplash()
    {
        _splashTick++;
        var dots = new string('.', _splashTick % 4);
        var msg = SplashMessages[(_splashTick / 4) % SplashMessages.Length];
        SplashStatus = msg + dots;
    }

    private void OpenProgressPage()
    {
        RaiseRank();
        SetModal("progress");
    }

    private void RaiseRank()
    {
        Raise(nameof(RankText));
        Raise(nameof(RankLetter));
        Raise(nameof(IsRanked));
        Raise(nameof(ScoreText));
        Raise(nameof(StreakText));
        Raise(nameof(CurrentStreakText));
        Raise(nameof(BestStreakText));
    }

    private void SelectGame(GameItem item)
    {
        _selected = item;
        _active = _state.Cooldowns.FirstOrDefault(c => Detector.SameGame(c.Game, item.Game));
        Raise(nameof(SelectedName));
        Raise(nameof(SelectedGame));
        Raise(nameof(PutOnPrompt));
        Raise(nameof(TakeOffPrompt));
        item.EnsureCover(460);
        SetModal(_active is not null ? "off" : "put");
    }

    private void PutOnCooldown()
    {
        if (_selected is null) return;
        var name = _selected.Name;
        var now = DateTime.Now.ToString("s");
        var installed = _games.Any(g => Detector.SameGame(g, _selected.Game, names: false))
                        || Detector.IsInstalled(_selected.Game, _games);
        var entry = new CooldownEntry
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Game = _selected.Game,
            CreatedAt = now,
            LastFiredAt = now,
            LastSeenInstalled = installed,
        };
        var already = _state.Cooldowns.Any(c => c.Enabled);
        _state.Cooldowns.Add(entry);
        Rewards.NoteCooldownStarted(_state, already);
        Storage.Save(_state);
        Task.Run(() => Scheduler.EnsureBackgroundTasks(_state));
        CloseModal();
        RebuildRows();
        RaiseRank();
        ShowToast($"{name} is on cooldown.");
    }

    private void TakeOffCooldown()
    {
        if (_active is null) return;
        var name = _active.Game.Name;
        Rewards.NoteTakenOff(_state);
        _state.Cooldowns = _state.Cooldowns.Where(c => c.Id != _active.Id).ToList();
        Storage.Save(_state);
        Task.Run(() => Scheduler.EnsureBackgroundTasks(_state));
        CloseModal();
        RebuildRows();
        RaiseRank();
        ShowToast($"{name} is off cooldown.");
    }

    public void HideGame(GameItem item)
    {
        if (!_state.HiddenIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
            _state.HiddenIds.Add(item.Id);
        Storage.Save(_state);
        RebuildRows();
        ShowToast($"Hid {item.Name}.");
    }

    public void UnhideGame(GameItem item)
    {
        _state.HiddenIds = _state.HiddenIds
            .Where(id => !id.Equals(item.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Storage.Save(_state);
        RebuildRows();
        ShowToast($"Unhid {item.Name}.");
    }

    public void MarkNotAGame(GameItem item)
    {
        if (!_state.IgnoredIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
            _state.IgnoredIds.Add(item.Id);
        if (!_state.IgnoredNames.Contains(item.Name, StringComparer.OrdinalIgnoreCase))
            _state.IgnoredNames.Add(item.Name);
        _state.HiddenIds = _state.HiddenIds
            .Where(id => !id.Equals(item.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _state.CustomGames = _state.CustomGames
            .Where(g => !g.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _state.Cooldowns = _state.Cooldowns
            .Where(c => !Detector.SameGame(c.Game, item.Game))
            .ToList();
        _games = _games.Where(g => !Detector.SameGame(g, item.Game)).ToList();
        _state.KnownGames = _games;
        Storage.Save(_state);
        CloseModal();
        RebuildRows();
        ShowToast($"{item.Name} is gone from the list.");
    }

    public void ToggleHidden()
    {
        ShowHidden = !ShowHidden;
        RebuildRows();
    }

    public void ToggleCooldownsOnTop()
    {
        CooldownsOnTop = !CooldownsOnTop;
        Storage.Save(_state);
        RebuildRows();
    }

    public void OpenSettings()
    {
        RefreshSettingsLists();
        SetModal("settings");
    }

    public void SetWatchEnabled(WatchFolderItem item, bool enabled)
    {
        if (_suspendWatch) return;
        if (item.IsCustom)
            SetDisabled(_state.DisabledScanDirs, item.Key, !enabled);
        else
            SetDisabled(_state.DisabledSources, item.Key, !enabled);
        Storage.Save(_state);
        Rescan(silent: true);
    }

    public void RemoveWatchFolder(WatchFolderItem item)
    {
        if (!item.IsCustom) return;
        _state.CustomScanDirs.RemoveAll(dir => dir.Equals(item.Key, StringComparison.OrdinalIgnoreCase));
        _state.DisabledScanDirs.RemoveAll(dir => dir.Equals(item.Key, StringComparison.OrdinalIgnoreCase));
        DropCustomGamesUnder(item.Key);
        Storage.Save(_state);
        RefreshSettingsLists();
        Rescan(silent: true);
    }

    public void RemoveFromBlacklist(BlacklistItem item)
    {
        if (!string.IsNullOrEmpty(item.Id))
        {
            _state.IgnoredIds = _state.IgnoredIds
                .Where(id => !id.Equals(item.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        if (!string.IsNullOrEmpty(item.Name))
        {
            _state.IgnoredNames = _state.IgnoredNames
                .Where(name => !name.Equals(item.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        Storage.Save(_state);
        RefreshSettingsLists();
        Rescan(silent: true);
        ShowToast($"{item.Name} can show up again.");
    }

    private void RefreshSettingsLists()
    {
        _suspendWatch = true;
        WatchFolders.Clear();
        var disabled = _state.DisabledSources.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in Detector.WatchSources)
        {
            if (!Detector.SourceAvailable(source)
                && !disabled.Contains(source)
                && !_games.Any(g => g.Source.Equals(source, StringComparison.OrdinalIgnoreCase)))
                continue;
            WatchFolders.Add(new WatchFolderItem(
                this, source, Detector.WatchLabel(source), custom: false,
                enabled: !disabled.Contains(source)));
        }
        foreach (var dir in _state.CustomScanDirs)
        {
            WatchFolders.Add(new WatchFolderItem(
                this, dir, dir, custom: true,
                enabled: !_state.DisabledScanDirs.Contains(dir, StringComparer.OrdinalIgnoreCase)));
        }
        _suspendWatch = false;

        Blacklist.Clear();
        var ids = _state.IgnoredIds;
        var names = _state.IgnoredNames;
        var count = Math.Max(ids.Count, names.Count);
        for (var i = 0; i < count; i++)
        {
            var id = i < ids.Count ? ids[i] : "";
            var name = i < names.Count ? names[i] : id;
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name)) continue;
            Blacklist.Add(new BlacklistItem(id, string.IsNullOrWhiteSpace(name) ? id : name));
        }
        Raise(nameof(HasBlacklist));
    }

    private static void SetDisabled(List<string> list, string key, bool disabled)
    {
        var has = list.Contains(key, StringComparer.OrdinalIgnoreCase);
        if (disabled && !has) list.Add(key);
        if (!disabled && has)
        {
            list.RemoveAll(item => item.Equals(key, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void DropCustomGamesUnder(string root)
    {
        _state.CustomGames = _state.CustomGames
            .Where(g => !PathIsUnder(g.InstallPath, root))
            .ToList();
    }

    private bool BelongsToCustomScanDir(string path) =>
        _state.CustomScanDirs.Any(dir => PathIsUnder(path, dir))
        || _state.DisabledScanDirs.Any(dir => PathIsUnder(path, dir));

    private static bool PathIsUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\', '/');
            var baseDir = Path.GetFullPath(root).TrimEnd('\\', '/');
            if (full.Equals(baseDir, StringComparison.OrdinalIgnoreCase)) return true;
            return full.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void AddFolderAsGame()
    {
        var path = PickFolder("Choose the game folder");
        if (string.IsNullOrEmpty(path)) return;
        var game = Detector.FromFolder(path);
        ForgetIgnore(game);
        if (_games.Any(g => g.Id.Equals(game.Id, StringComparison.OrdinalIgnoreCase)
                            || PathsMatch(g.InstallPath, game.InstallPath)))
        {
            ShowToast($"{game.Name} is already in the list.");
            CloseModal();
            return;
        }
        RememberCustom(game);
        _games = Detector.Combine(_games, [game]);
        _state.KnownGames = _games;
        Storage.Save(_state);
        CloseModal();
        RebuildRows();
        ShowToast($"Added {game.Name}.");
    }

    private void ScanCustomFolder()
    {
        var path = PickFolder("Choose a folder to scan");
        if (string.IsNullOrEmpty(path)) return;
        var full = Path.GetFullPath(path);
        if (!_state.CustomScanDirs.Contains(full, StringComparer.OrdinalIgnoreCase))
            _state.CustomScanDirs.Add(full);
        CloseModal();
        Scanning = true;
        RescanLabel = "Scanning";
        Task.Run(() =>
        {
            var found = Detector.ScanDirectory(full);
            _ui?.BeginInvoke(() => FinishCustomScan(found, full));
        });
    }

    private void FinishCustomScan(List<Game> found, string folder)
    {
        foreach (var game in found)
            ForgetIgnore(game);
        DropCustomGamesUnder(folder);
        _scanGen++;
        _games = CollectGames();
        _state.KnownGames = _games;
        Storage.Save(_state);
        Scanning = false;
        RescanLabel = "Rescan";
        RebuildRows();
        var label = string.IsNullOrEmpty(folder) ? "that folder" : Path.GetFileName(folder.TrimEnd('\\', '/'));
        ShowToast(found.Count == 0
            ? $"No games found in {label}."
            : $"Found {found.Count} game{(found.Count == 1 ? "" : "s")} in {label}.");
    }

    private static string? PickFolder(string title)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void RememberCustom(Game game)
    {
        _state.CustomGames = _state.CustomGames
            .Where(g => !g.Id.Equals(game.Id, StringComparison.OrdinalIgnoreCase))
            .Append(game)
            .ToList();
    }

    private void ForgetIgnore(Game game)
    {
        _state.IgnoredIds = _state.IgnoredIds
            .Where(id => !id.Equals(game.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _state.IgnoredNames = _state.IgnoredNames
            .Where(name => !name.Equals(game.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private bool IsIgnored(Game game) =>
        _state.IgnoredIds.Contains(game.Id, StringComparer.OrdinalIgnoreCase)
        || _state.IgnoredNames.Contains(game.Name, StringComparer.OrdinalIgnoreCase);

    private static bool PathsMatch(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd('\\', '/'),
                Path.GetFullPath(right).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public void CloseModal()
    {
        SetModal("none");
        _selected = null;
        _active = null;
    }

    private void SetModal(string kind)
    {
        _modal = kind;
        Raise(nameof(ModalOpen));
        Raise(nameof(ModalPutOn));
        Raise(nameof(ModalTakeOff));
        Raise(nameof(ModalProgress));
        Raise(nameof(ModalSettings));
        Raise(nameof(ModalTitle));
        Raise(nameof(SelectedName));
        Raise(nameof(SelectedGame));
        Raise(nameof(PutOnPrompt));
        Raise(nameof(TakeOffPrompt));
        Raise(nameof(ParentEnabled));
        Raise(nameof(RescanEnabled));
    }

    private void ShowToast(string message)
    {
        Toast = message;
        ToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _can;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? can = null)
    {
        _execute = execute;
        _can = can;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}
