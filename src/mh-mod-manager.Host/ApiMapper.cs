using MhModManager.Core;
using MhModManager.Core.Equipment;
using MhModManager.Models;

namespace MhModManager.Host;

internal static class ApiMapper
{
    public static BootstrapDto Bootstrap(ModService service, SettingsStore settings)
    {
        var games = GameProfile.All.Select(profile => MapGame(service, profile)).ToList();
        var last = settings.Current.LastGame;
        var selected = games.Any(game => game.Id == last) ? last : games[0].Id;
        return new BootstrapDto
        {
            Settings = MapSettings(settings.Current),
            Games = games,
            Workspace = Workspace(service, settings, selected)
        };
    }

    public static WorkspaceDto Workspace(ModService service, SettingsStore settings, GameId gameId)
    {
        var game = GameProfile.Get(gameId);
        var groups = service.GetGroups(game).OrderBy(group => group.Index).ToList();
        var mods = service.GetMods(game);
        return new WorkspaceDto
        {
            Game = MapGame(service, game),
            Settings = MapSettings(settings.Current),
            Groups = groups.Select(group => MapGroup(group, mods)).ToList(),
            Mods = mods
                .OrderBy(mod => groups.FindIndex(group => group.Id == mod.GroupId))
                .ThenBy(mod => mod.Index)
                .Select(mod => MapMod(service, game, mod, groups))
                .ToList()
        };
    }

    private static GameDto MapGame(ModService service, GameProfile profile)
    {
        var path = service.ResolveGamePath(profile) ?? "";
        var installed = SteamLocator.IsGameDir(path, profile.ExeName);
        return new GameDto
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            ShortName = profile.ShortName,
            SteamAppId = profile.SteamAppId,
            ExeName = profile.ExeName,
            Path = string.IsNullOrWhiteSpace(path) ? "" : path,
            Installed = installed,
            UsesPakPatches = profile.UsesPakPatches,
            DeployRoot = profile.Id == GameId.World ? "nativePC" : "natives / reframework",
            PakState = profile.UsesPakPatches ? "PAK 部署到 pak_mods 文件夹" : "nativePC 文件覆盖模式"
        };
    }

    private static SettingsDto MapSettings(AppSettings settings) => new()
    {
        LastGame = settings.LastGame,
        CheckGameRunning = settings.CheckGameRunning,
        InstallOption = settings.InstallOption
    };

    private static GroupDto MapGroup(ModGroup group, IEnumerable<ModRecord> mods)
    {
        var members = mods.Where(mod => mod.GroupId == group.Id).ToList();
        return new GroupDto
        {
            Id = group.Id,
            Name = group.Name,
            Index = group.Index,
            IsDefault = group.IsDefault,
            Collapsed = group.Collapsed,
            EnabledCount = members.Count(mod => mod.Enabled),
            TotalCount = members.Count
        };
    }

    private static ModDto MapMod(ModService service, GameProfile game, ModRecord mod, IReadOnlyList<ModGroup> groups)
    {
        var preview = service.PreviewPath(game, mod);
        var filesDir = AppPaths.ModFilesDir(game.SteamAppId, mod.Id);
        var equipment = ResolveEquipment(service, game, mod, filesDir);
        var covers = new Dictionary<int, string>();
        foreach (var component in mod.Components)
        {
            covers[component.Id] = service.ComponentPreviewPath(game, mod, component.Id);
        }

        return new ModDto
        {
            Id = mod.Id,
            GroupId = mod.GroupId,
            Name = string.IsNullOrWhiteSpace(mod.DisplayName) ? mod.Name : mod.DisplayName,
            Version = string.IsNullOrWhiteSpace(mod.Version) ? "未标注版本" : $"v{mod.Version}",
            Author = string.IsNullOrWhiteSpace(mod.Author) ? "未知作者" : mod.Author,
            Category = string.IsNullOrWhiteSpace(mod.Category) ? "其他" : mod.Category,
            Enabled = mod.Enabled,
            Index = mod.Index,
            FileCount = mod.Files.Count,
            HasPreview = !string.IsNullOrWhiteSpace(preview),
            PreviewUrl = string.IsNullOrWhiteSpace(preview) ? "" : $"/api/games/{game.Id}/mods/{mod.Id}/preview?t={mod.InstallTime.ToUnixTimeSeconds()}",
            HomeUrl = mod.HomeUrl,
            InstalledAt = mod.InstallTime == default ? "未知时间" : mod.InstallTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            GroupName = groups.FirstOrDefault(group => group.Id == mod.GroupId)?.Name ?? "未分组",
            IsBundle = mod.IsBundle,
            Components = mod.Components.Select(component => new ComponentDto
            {
                Id = component.Id,
                Name = component.Name,
                Enabled = component.Enabled,
                FileCount = component.Files.Count,
                HasPreview = !string.IsNullOrWhiteSpace(covers.GetValueOrDefault(component.Id)),
                PreviewUrl = covers.TryGetValue(component.Id, out var cover) && !string.IsNullOrWhiteSpace(cover)
                    ? $"/api/games/{game.Id}/mods/{mod.Id}/preview?component={component.Id}&t={mod.InstallTime.ToUnixTimeSeconds()}"
                    : ""
            }).ToList(),
            Equipment = equipment.Select(item => new EquipmentDto
            {
                Kind = item.Kind,
                Id = item.Id,
                Type = item.Type,
                Name = item.Name,
                IsPfb = item.IsPfb,
                IsPak = item.IsPak,
                FileCount = item.FileCount,
                Display = item.Display
            }).ToList()
        };
    }

    /// <summary>组件化记录按组件目录逐个解析装备（组件内路径不带前缀），普通记录原样解析。</summary>
    private static List<EquipmentMatch> ResolveEquipment(ModService service, GameProfile game, ModRecord mod, string filesDir)
    {
        if (!mod.IsBundle)
        {
            return EquipmentResolver.Resolve(game.Id, mod.Files, Directory.Exists(filesDir) ? filesDir : null).ToList();
        }

        return mod.Components.SelectMany(component =>
        {
            var prefix = $"c{component.Id}/";
            var baseDir = Path.Combine(filesDir, prefix);
            var files = component.Files
                .Where(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(file => file[prefix.Length..]);
            return EquipmentResolver.Resolve(game.Id, files, Directory.Exists(baseDir) ? baseDir : null);
        }).ToList();
    }
}

internal sealed class BootstrapDto
{
    public SettingsDto Settings { get; set; } = new();
    public List<GameDto> Games { get; set; } = [];
    public WorkspaceDto Workspace { get; set; } = new();
    public string Status { get; set; } = "就绪";
    public bool Error { get; set; }
}

internal sealed class WorkspaceDto
{
    public GameDto Game { get; set; } = new();
    public SettingsDto Settings { get; set; } = new();
    public List<GroupDto> Groups { get; set; } = [];
    public List<ModDto> Mods { get; set; } = [];
    public string Status { get; set; } = "就绪";
    public bool Error { get; set; }
}

internal sealed class GameDto
{
    public GameId Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string ShortName { get; set; } = "";
    public int SteamAppId { get; set; }
    public string ExeName { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Installed { get; set; }
    public bool UsesPakPatches { get; set; }
    public string DeployRoot { get; set; } = "";
    public string PakState { get; set; } = "";
}

internal sealed class SettingsDto
{
    public GameId LastGame { get; set; }
    public bool CheckGameRunning { get; set; }
    public int InstallOption { get; set; }
}

internal sealed class GroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Index { get; set; }
    public bool IsDefault { get; set; }
    public bool Collapsed { get; set; }
    public int EnabledCount { get; set; }
    public int TotalCount { get; set; }
}

internal sealed class ModDto
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Category { get; set; } = "";
    public bool Enabled { get; set; }
    public int Index { get; set; }
    public int FileCount { get; set; }
    public bool HasPreview { get; set; }
    public string PreviewUrl { get; set; } = "";
    public string HomeUrl { get; set; } = "";
    public string InstalledAt { get; set; } = "";
    public string GroupName { get; set; } = "";
    public bool IsBundle { get; set; }
    public List<ComponentDto> Components { get; set; } = [];
    public List<EquipmentDto> Equipment { get; set; } = [];
}

internal sealed class ComponentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public int FileCount { get; set; }
    public bool HasPreview { get; set; }
    public string PreviewUrl { get; set; } = "";
}

internal sealed class EquipmentDto
{
    public string Kind { get; set; } = "";
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPfb { get; set; }
    public bool IsPak { get; set; }
    public int FileCount { get; set; }
    public string Display { get; set; } = "";
}
