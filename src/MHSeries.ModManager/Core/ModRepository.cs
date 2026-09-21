using MhModManager.Models;

namespace MhModManager.Core;

public static class ModRepository
{
    public static List<ModRecord> LoadAll(GameProfile game)
    {
        var dir = AppPaths.ModsDir(game.SteamAppId);
        Directory.CreateDirectory(dir);
        var mods = new List<ModRecord>();

        foreach (var folder in Directory.GetDirectories(dir))
        {
            var info = Path.Combine(folder, "info.json");
            if (!File.Exists(info))
            {
                continue;
            }

            var mod = JsonUtil.Load<ModRecord?>(info, null);
            if (mod is null || mod.Id <= 0)
            {
                continue;
            }

            var filesDir = AppPaths.ModFilesDir(game.SteamAppId, mod.Id);
            if (Directory.Exists(filesDir))
            {
                var previousFiles = mod.Files.ToHashSet(StringComparer.OrdinalIgnoreCase);
                RefreshFiles(game, mod);
                var filesChanged = !previousFiles.SetEquals(mod.Files);
                var removedMappings = mod.OverwriteFiles.Keys
                    .Where(key => !mod.Files.Contains(key, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                foreach (var key in removedMappings)
                {
                    mod.OverwriteFiles.Remove(key);
                }

                if (filesChanged || removedMappings.Count > 0)
                {
                    Save(game, mod);
                }
            }

            mods.Add(mod);
        }

        return mods;
    }

    public static List<ModGroup> LoadGroups(GameProfile game, List<ModRecord> mods)
    {
        var groups = JsonUtil.Load(AppPaths.GroupsFile(game.SteamAppId), new List<ModGroup>());
        var defaultGroup = groups.FirstOrDefault(group => group.IsDefault);
        if (defaultGroup is null)
        {
            defaultGroup = new ModGroup
            {
                Id = groups.Count == 0 ? 1 : groups.Max(group => group.Id) + 1,
                Name = "未分组",
                Index = groups.Count == 0 ? 1 : groups.Max(group => group.Index) + 1,
                IsDefault = true
            };
            groups.Add(defaultGroup);
            SaveGroups(game, groups);
        }

        var validIds = groups.Select(group => group.Id).ToHashSet();
        var migrated = false;
        foreach (var mod in mods.Where(item => item.GroupId <= 0 || !validIds.Contains(item.GroupId)))
        {
            mod.GroupId = defaultGroup.Id;
            Save(game, mod);
            migrated = true;
        }

        if (migrated)
        {
            FixIndex(game, mods, groups);
        }

        groups.Sort((left, right) => left.Index.CompareTo(right.Index));
        return groups;
    }

    public static void SaveGroups(GameProfile game, IReadOnlyList<ModGroup> groups)
    {
        JsonUtil.Save(AppPaths.GroupsFile(game.SteamAppId), groups);
    }

    public static void Save(GameProfile game, ModRecord mod)
    {
        var dir = AppPaths.ModDir(game.SteamAppId, mod.Id);
        Directory.CreateDirectory(dir);
        JsonUtil.Save(Path.Combine(dir, "info.json"), mod);
    }

    public static void Delete(GameProfile game, ModRecord mod)
    {
        var dir = AppPaths.ModDir(game.SteamAppId, mod.Id);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }

    public static void RefreshFiles(GameProfile game, ModRecord mod)
    {
        var filesDir = AppPaths.ModFilesDir(game.SteamAppId, mod.Id);
        mod.Files.Clear();
        if (!Directory.Exists(filesDir))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(filesDir, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (IsPreviewFileName(name) && Path.GetDirectoryName(file) == filesDir)
            {
                continue;
            }

            mod.Files.Add(Path.GetRelativePath(filesDir, file).Replace('\\', '/'));
        }

        // 组件化记录：组件文件列表对账到磁盘实际存在的内容。
        foreach (var component in mod.Components)
        {
            component.Files.RemoveAll(file => !mod.Files.Contains(file, StringComparer.OrdinalIgnoreCase));
        }
    }

    public static void MakeId(IReadOnlyList<ModRecord> mods, IReadOnlyList<ModGroup> groups, ModRecord mod)
    {
        var maxId = mods.Count == 0 ? 1000 : Math.Max(1000, mods.Max(item => item.Id));
        var defaultGroup = groups.First(group => group.IsDefault);
        var groupMods = mods.Where(item => item.GroupId == defaultGroup.Id).ToList();
        mod.Id = maxId + 1;
        mod.GroupId = defaultGroup.Id;
        mod.Index = groupMods.Count == 0 ? 1 : groupMods.Max(item => item.Index) + 1;
    }

    public static void FixIndex(GameProfile game, List<ModRecord> mods, IReadOnlyList<ModGroup> groups)
    {
        foreach (var group in groups)
        {
            var members = mods.Where(mod => mod.GroupId == group.Id).OrderBy(mod => mod.Index).ThenBy(mod => mod.Id).ToList();
            for (var i = 0; i < members.Count; i++)
            {
                if (members[i].Index == i + 1)
                {
                    continue;
                }

                members[i].Index = i + 1;
                Save(game, members[i]);
            }
        }

        mods.Sort((left, right) => GroupOrder.Compare(left, right, groups));
    }

    public static void FixGroupIndex(GameProfile game, List<ModGroup> groups)
    {
        groups.Sort((left, right) => left.Index.CompareTo(right.Index));
        for (var i = 0; i < groups.Count; i++)
        {
            groups[i].Index = i + 1;
        }

        SaveGroups(game, groups);
    }

    public static bool IsPreviewFileName(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        return stem.Equals("screenshot", StringComparison.OrdinalIgnoreCase)
               || stem.Equals("preview", StringComparison.OrdinalIgnoreCase)
               || stem.StartsWith("cover", StringComparison.OrdinalIgnoreCase);
    }
}
