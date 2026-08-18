using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HuntForge.Core;
using HuntForge.Models;

namespace HuntForge.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore _settings = new();
    private readonly ModService _service;
    private GameViewModel? _selectedGame;
    private ModItemViewModel? _selectedMod;
    private string _search = "";
    private string _status = "就绪";
    private bool _isBusy;
    private bool _showSettings;
    private bool _checkRunning;
    private bool _fixPakNumbers;
    private int _installOption;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GameViewModel> Games { get; } = [];
    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public ObservableCollection<GroupViewModel> Groups { get; } = [];
    public ObservableCollection<GroupViewModel> FilteredGroups { get; } = [];

    public GameViewModel? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (ReferenceEquals(_selectedGame, value)) return;
            if (_selectedGame is not null)
            {
                _selectedGame.IsSelected = false;
            }
            _selectedGame = value;
            if (_selectedGame is not null)
            {
                _selectedGame.IsSelected = true;
            }
            OnPropertyChanged();
            if (value is not null)
            {
                _settings.Current.LastGame = value.Profile.Id;
                _settings.Save();
                LoadSelectedGame();
            }
        }
    }

    public ModItemViewModel? SelectedMod
    {
        get => _selectedMod;
        set { _selectedMod = value; OnPropertyChanged(); }
    }

    public string Search
    {
        get => _search;
        set { _search = value; OnPropertyChanged(); ApplyFilter(); }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; OnPropertyChanged(); }
    }

    public bool ShowSettings
    {
        get => _showSettings;
        set { _showSettings = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsInfoVisible)); }
    }

    public bool IsInfoVisible => !ShowSettings;

    public bool CheckRunning
    {
        get => _checkRunning;
        set { _checkRunning = value; _settings.Current.CheckGameRunning = value; _settings.Save(); OnPropertyChanged(); }
    }

    public bool FixPakNumbers
    {
        get => _fixPakNumbers;
        set { _fixPakNumbers = value; _settings.Current.FixPakNumber = value; _settings.Save(); OnPropertyChanged(); }
    }

    public int InstallOption
    {
        get => _installOption;
        set { _installOption = value; _settings.Current.InstallOption = value; _settings.Save(); OnPropertyChanged(); }
    }

    public int TotalMods => Mods.Count;
    public int EnabledMods => Mods.Count(m => m.Enabled);
    public string GamePath => SelectedGame?.GamePath ?? "未设置游戏目录";
    public string DeployRoot => SelectedGame is null ? "" : SelectedGame.Profile.Id == GameId.World ? "nativePC" : "natives / reframework";
    public string GameState => SelectedGame?.IsInstalled == true ? "已找到游戏" : "等待设置路径";
    public string PakState => SelectedGame?.Profile.UsesPakPatches == true ? "PAK 补丁编号自动管理" : "nativePC 文件覆盖模式";
    public string EnableSummary => $"{EnabledMods} / {TotalMods} 已启用";

    public ICommand RefreshCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand CleanCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand SelectFolderCommand { get; }
    public ICommand CreateGroupCommand { get; }

    public MainViewModel()
    {
        _service = new ModService(_settings);
        _settings.Load();
        _checkRunning = _settings.Current.CheckGameRunning;
        _fixPakNumbers = _settings.Current.FixPakNumber;
        _installOption = _settings.Current.InstallOption;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        ImportCommand = new AsyncRelayCommand(ImportAsync, () => !IsBusy && SelectedGame is not null);
        LaunchCommand = new RelayCommand(Launch, () => SelectedGame?.IsInstalled == true && !IsBusy);
        CleanCommand = new AsyncRelayCommand(CleanAsync, () => !IsBusy && SelectedGame?.IsInstalled == true);
        SettingsCommand = new RelayCommand(() => ShowSettings = !ShowSettings);
        SelectFolderCommand = new AsyncRelayCommand(SelectFolderAsync, () => SelectedGame is not null);
        CreateGroupCommand = new RelayCommand(CreateGroup, () => !IsBusy && SelectedGame is not null);

        foreach (var profile in GameProfile.All)
        {
            Games.Add(new GameViewModel(profile, _service, this));
        }

        SelectedGame = Games.FirstOrDefault(g => g.Profile.Id == _settings.Current.LastGame) ?? Games[0];
    }

    public async Task ImportFromPathAsync(string path)
    {
        if (IsBusy || SelectedGame is null || string.IsNullOrWhiteSpace(path)) return;
        var game = SelectedGame.Profile;
        try
        {
            IsBusy = true;
            Status = "正在解析 MOD 结构...";
            var progress = new Progress<int>(p => Status = $"正在解析 MOD 结构 {p}%");
            var parsed = await Task.Run(() => _service.ParseImport(game, path, progress));
            var installed = await Task.Run(() => _service.Install(game, parsed));
            LoadSelectedGame();
            Status = $"已导入 {installed.DisplayName}";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    public async Task ToggleModAsync(ModItemViewModel item, bool enabled)
    {
        if (IsBusy || SelectedGame is null) return;
        var game = SelectedGame.Profile;
        try
        {
            IsBusy = true;
            Status = enabled ? $"正在启用 {item.Name}..." : $"正在禁用 {item.Name}...";
            await Task.Run(() => _service.SetEnabled(game, item.Record, enabled));
            LoadSelectedGame();
            Status = enabled ? $"已启用 {item.Name}" : $"已禁用 {item.Name}";
        }
        catch (Exception ex)
        {
            item.SetEnabledSilently(item.Record.Enabled);
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    public async Task RemoveModAsync(ModItemViewModel item)
    {
        if (IsBusy || SelectedGame is null) return;
        var game = SelectedGame.Profile;
        try
        {
            IsBusy = true;
            await Task.Run(() => _service.Uninstall(game, item.Record));
            LoadSelectedGame();
            Status = $"已卸载 {item.Name}";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    public async Task ToggleGroupAsync(GroupViewModel group, bool enabled)
    {
        if (IsBusy || SelectedGame is null) return;
        var game = SelectedGame.Profile;
        try
        {
            IsBusy = true;
            Status = enabled ? $"正在启用 {group.Name}..." : $"正在禁用 {group.Name}...";
            await Task.Run(() => _service.SetGroupEnabled(game, group.Group, enabled));
            LoadSelectedGame();
            Status = enabled ? $"已启用分组 {group.Name}" : $"已禁用分组 {group.Name}";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    public async Task MoveGroupAsync(GroupViewModel group, int delta)
    {
        if (IsBusy || SelectedGame is null) return;
        var game = SelectedGame.Profile;
        try
        {
            IsBusy = true;
            await Task.Run(() => _service.MoveGroup(game, group.Group, delta));
            LoadSelectedGame();
            Status = "分组顺序已更新";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    public async Task DeleteGroupAsync(GroupViewModel group)
    {
        if (IsBusy || SelectedGame is null) return;
        var game = SelectedGame.Profile;
        try
        {
            IsBusy = true;
            await Task.Run(() => _service.DeleteGroup(game, group.Group));
            LoadSelectedGame();
            Status = $"已删除分组 {group.Name}";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    public void RenameGroup(GroupViewModel group, string name)
    {
        if (SelectedGame is null) return;
        try
        {
            _service.RenameGroup(SelectedGame.Profile, group.Group, name);
            group.RefreshName();
            Status = "分组已重命名";
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    public async Task MoveModToGroupAsync(ModItemViewModel item, ModGroup group)
    {
        if (IsBusy || SelectedGame is null || item.Record.GroupId == group.Id) return;
        var game = SelectedGame.Profile;
        try
        {
            IsBusy = true;
            await Task.Run(() => _service.MoveModToGroup(game, item.Record, group.Id));
            LoadSelectedGame();
            Status = $"已将 {item.Name} 移到 {group.Name}";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    public async Task MoveModAsync(ModItemViewModel item, int delta)
    {
        if (IsBusy || SelectedGame is null) return;
        var game = SelectedGame.Profile;
        try
        {
            IsBusy = true;
            Status = "正在更新 MOD 优先级...";
            await Task.Run(() => _service.Move(game, item.Record, delta));
            LoadSelectedGame();
            Status = "MOD 优先级已更新";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    public IReadOnlyList<ModRecord> Conflicts(ModItemViewModel item) =>
        SelectedGame is null ? [] : _service.FindConflicts(SelectedGame.Profile, item.Record);

    public void SetGamePath(string path)
    {
        if (IsBusy || SelectedGame is null) return;
        _settings.SetGamePath(SelectedGame.Profile, path);
        SelectedGame.Refresh();
        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(GameState));
        NotifyCommands();
    }

    private async Task RefreshAsync()
    {
        if (IsBusy || SelectedGame is null) return;
        var game = SelectedGame.Profile;
        try
        {
            IsBusy = true;
            Status = "正在刷新 MOD 列表...";
            await Task.Run(() => _service.Invalidate(game));
            LoadSelectedGame();
            Status = "列表已刷新";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    private async Task ImportAsync()
    {
        Status = "请使用窗口中的文件选择器导入 MOD";
        await Task.CompletedTask;
    }

    private void Launch()
    {
        if (SelectedGame is null) return;
        try { _service.Launch(SelectedGame.Profile); Status = "已通过 Steam 启动游戏"; }
        catch (Exception ex) { Status = ex.Message; }
    }

    private async Task CleanAsync()
    {
        if (IsBusy || SelectedGame is null) return;
        var game = SelectedGame.Profile;
        try
        {
            IsBusy = true;
            await Task.Run(() => _service.CleanDeployed(game, false));
            LoadSelectedGame();
            Status = "已清理当前启用的 MOD 文件";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; NotifyCommands(); }
    }

    private async Task SelectFolderAsync()
    {
        Status = "请在窗口中选择游戏目录";
        await Task.CompletedTask;
    }

    private void LoadSelectedGame()
    {
        if (SelectedGame is null) return;
        SelectedGame.Refresh();
        var groups = _service.GetGroups(SelectedGame.Profile);
        var records = _service.GetMods(SelectedGame.Profile);
        Mods.Clear();
        Groups.Clear();
        foreach (var group in groups.OrderBy(item => item.Index))
        {
            var members = records.Where(record => record.GroupId == group.Id)
                .OrderBy(record => record.Index)
                .Select(record => new ModItemViewModel(record, this, groups))
                .ToList();
            foreach (var member in members)
            {
                Mods.Add(member);
            }

            Groups.Add(new GroupViewModel(group, members, this));
        }
        ApplyFilter();
        OnPropertyChanged(nameof(TotalMods));
        OnPropertyChanged(nameof(EnabledMods));
        OnPropertyChanged(nameof(EnableSummary));
        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(GameState));
        OnPropertyChanged(nameof(PakState));
        OnPropertyChanged(nameof(DeployRoot));
    }

    private void ApplyFilter()
    {
        FilteredGroups.Clear();
        var query = Search.Trim();
        foreach (var group in Groups)
        {
            group.ApplyFilter(query);
            if (string.IsNullOrWhiteSpace(query) || group.Mods.Count > 0)
            {
                FilteredGroups.Add(group);
            }
        }
        OnPropertyChanged(nameof(FilteredGroups));
    }

    private void CreateGroup()
    {
        if (SelectedGame is null) return;
        try
        {
            var group = _service.CreateGroup(SelectedGame.Profile, "新分组");
            LoadSelectedGame();
            Status = $"已创建 {group.Name}";
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    private void NotifyCommands()
    {
        (RefreshCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (ImportCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (CleanCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (LaunchCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CreateGroupCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GameViewModel : INotifyPropertyChanged
{
    private readonly ModService _service;
    private readonly MainViewModel _owner;
    private string? _gamePath;
    private bool _isInstalled;
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;
    public GameProfile Profile { get; }
    public string GamePath => _gamePath ?? "";
    public bool IsInstalled => _isInstalled;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }
    public string Status => IsInstalled ? "已安装" : "未配置";
    public string Initial => Profile.ShortName[..1];

    public GameViewModel(GameProfile profile, ModService service, MainViewModel owner)
    {
        Profile = profile;
        _service = service;
        _owner = owner;
        Refresh();
    }

    public void Refresh()
    {
        _gamePath = _service.ResolveGamePath(Profile);
        _isInstalled = SteamLocator.IsGameDir(_gamePath, Profile.ExeName);
        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(Status));
    }

    public void Select() => _owner.SelectedGame = this;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GroupViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _owner;
    private readonly List<ModItemViewModel> _allMods;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ModGroup Group { get; }
    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public string Name => Group.Name;
    public bool IsDefault => Group.IsDefault;
    public bool CanDelete => !Group.IsDefault;
    public bool AllEnabled => _allMods.Count > 0 && _allMods.All(mod => mod.Enabled);
    public string Summary => $"{_allMods.Count(mod => mod.Enabled)} / {_allMods.Count} 已启用";

    public ICommand ToggleCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }
    public ICommand DeleteCommand { get; }

    public GroupViewModel(ModGroup group, IEnumerable<ModItemViewModel> mods, MainViewModel owner)
    {
        Group = group;
        _owner = owner;
        _allMods = mods.ToList();
        foreach (var mod in _allMods)
        {
            Mods.Add(mod);
        }

        ToggleCommand = new AsyncRelayCommand(() => _owner.ToggleGroupAsync(this, !AllEnabled));
        UpCommand = new AsyncRelayCommand(() => _owner.MoveGroupAsync(this, -1));
        DownCommand = new AsyncRelayCommand(() => _owner.MoveGroupAsync(this, 1));
        DeleteCommand = new AsyncRelayCommand(() => _owner.DeleteGroupAsync(this));
    }

    public void ApplyFilter(string query)
    {
        Mods.Clear();
        foreach (var mod in _allMods.Where(item => string.IsNullOrWhiteSpace(query) ||
                                                   item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                                   item.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                                   item.Author.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            Mods.Add(mod);
        }
    }

    public void RefreshName() => OnPropertyChanged(nameof(Name));

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class ModItemViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _owner;
    private bool _enabled;
    private ModGroup _selectedGroup;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ModRecord Record { get; }
    public IReadOnlyList<ModGroup> GroupChoices { get; }
    public ModGroup SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (value is null || value.Id == Record.GroupId)
            {
                _selectedGroup = value ?? _selectedGroup;
                OnPropertyChanged();
                return;
            }

            _selectedGroup = value;
            OnPropertyChanged();
            _ = _owner.MoveModToGroupAsync(this, value);
        }
    }
    public string Name => Record.DisplayName;
    public string Version => string.IsNullOrWhiteSpace(Record.Version) ? "未标注版本" : $"v{Record.Version}";
    public string Author => string.IsNullOrWhiteSpace(Record.Author) ? "未知作者" : Record.Author;
    public string Category => string.IsNullOrWhiteSpace(Record.Category) ? "其他" : Record.Category;
    public string FileCount => $"{Record.Files.Count} 个文件";
    public string PakInfo => Record.OverwriteFiles.Count > 0 ? $"{Record.OverwriteFiles.Count} 个 PAK" : "";
    public bool HasPak => Record.OverwriteFiles.Count > 0;
    public bool Enabled => _enabled;
    public string EnabledLabel => Enabled ? "已启用" : "已禁用";
    public bool HasNexus => Record.NexusId > 0;

    public ICommand ToggleCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand DownCommand { get; }

    public ModItemViewModel(ModRecord record, MainViewModel owner, IReadOnlyList<ModGroup> groups)
    {
        Record = record;
        _owner = owner;
        _enabled = record.Enabled;
        GroupChoices = groups.OrderBy(group => group.Index).ToList();
        _selectedGroup = GroupChoices.FirstOrDefault(group => group.Id == record.GroupId) ?? GroupChoices[0];
        ToggleCommand = new AsyncRelayCommand(() => _owner.ToggleModAsync(this, !Enabled));
        RemoveCommand = new AsyncRelayCommand(() => _owner.RemoveModAsync(this));
        UpCommand = new AsyncRelayCommand(() => _owner.MoveModAsync(this, -1));
        DownCommand = new AsyncRelayCommand(() => _owner.MoveModAsync(this, 1));
    }

    public void SetEnabledSilently(bool value)
    {
        _enabled = value;
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(EnabledLabel));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
