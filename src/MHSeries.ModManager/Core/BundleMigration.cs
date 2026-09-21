using MhModManager.Models;

namespace MhModManager.Core;

/// <summary>
/// 旧版"一个压缩包拆成分组内多个 MOD"的数据迁移：
/// 非默认分组内所有成员的 SourceFile 同源（文件名一致，存在的文件大小一致）时，
/// 合并为一个组件化 MOD（组件 = 原成员，按组内顺序），分组删除。
/// 手动创建的分组不满足同源条件，不受影响。
/// </summary>
public static class BundleMigration
{
    public static bool Migrate(GameProfile game, List<ModRecord> mods, List<ModGroup> groups)
    {
        var migrated = false;
        foreach (var group in groups.Where(item => !item.IsDefault).ToList())
        {
            var members = mods.Where(item => item.GroupId == group.Id)
                .OrderBy(item => item.Index).ThenBy(item => item.Id)
                .ToList();
            if (members.Count < 2 || !SameSource(members))
            {
                continue;
            }

            try
            {
                MigrateGroup(game, mods, groups, group, members);
                migrated = true;
            }
            catch
            {
                // 单个分组迁移失败不影响其余数据，保持旧结构可用。
            }
        }

        if (migrated)
        {
            ModRepository.FixGroupIndex(game, groups);
            ModRepository.FixIndex(game, mods, groups);
        }

        return migrated;
    }

    private static bool SameSource(List<ModRecord> members)
    {
        // bundle 导入只有最后一个成员保留压缩包拷贝（存放在自身目录），其余是外部原路径；
        // 文件名一致 + 已存在文件的大小一致即可判定同一压缩包。
        var name = Path.GetFileName(members[0].SourceFile);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        long? length = null;
        foreach (var member in members)
        {
            if (string.IsNullOrWhiteSpace(member.SourceFile) ||
                !Path.GetFileName(member.SourceFile).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (File.Exists(member.SourceFile))
            {
                var size = new FileInfo(member.SourceFile).Length;
                if (length is not null && length != size)
                {
                    return false;
                }

                length = size;
            }
        }

        return true;
    }

    private static void MigrateGroup(GameProfile game, List<ModRecord> mods, List<ModGroup> groups, ModGroup group, List<ModRecord> members)
    {
        var keeper = members[0];
        var modDir = AppPaths.ModDir(game.SteamAppId, keeper.Id);
        var filesDir = AppPaths.ModFilesDir(game.SteamAppId, keeper.Id);
        var coversDir = Path.Combine(modDir, "covers");
        Directory.CreateDirectory(coversDir);

        var components = new List<ModComponent>();
        var files = new List<string>();
        var nextId = 1;
        foreach (var member in members)
        {
            var componentId = nextId++;
            var component = new ModComponent
            {
                Id = componentId,
                Name = string.IsNullOrWhiteSpace(member.DisplayName) ? member.Name : member.DisplayName,
                Enabled = member.Enabled
            };

            var memberFilesDir = AppPaths.ModFilesDir(game.SteamAppId, member.Id);
            if (Directory.Exists(memberFilesDir))
            {
                var targetDir = Path.Combine(filesDir, $"c{componentId}");
                if (member == keeper)
                {
                    // 保留者的 files/ 需要整体下沉到 c1/ 子目录。
                    var moving = Path.Combine(modDir, $"covers_moving_c{componentId}");
                    if (Directory.Exists(moving))
                    {
                        Directory.Delete(moving, true);
                    }

                    Directory.Move(memberFilesDir, moving);
                    Directory.CreateDirectory(filesDir);
                    Directory.Move(moving, targetDir);
                }
                else
                {
                    Directory.Move(memberFilesDir, targetDir);
                }

                foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
                {
                    var stored = $"c{componentId}/" + Path.GetRelativePath(targetDir, file).Replace('\\', '/');
                    component.Files.Add(stored);
                    files.Add(stored);
                }
            }

            foreach (var mapping in member.OverwriteFiles)
            {
                keeper.OverwriteFiles[$"c{componentId}/{mapping.Key}"] = mapping.Value;
            }

            if (!string.IsNullOrWhiteSpace(member.PreviewImage) && File.Exists(member.PreviewImage))
            {
                var cover = Path.Combine(coversDir, $"c{componentId}{Path.GetExtension(member.PreviewImage)}");
                File.Copy(member.PreviewImage, cover, true);
            }

            components.Add(component);
        }

        keeper.Files = files;
        keeper.Components = components;
        keeper.Enabled = members.Any(member => member.Enabled);
        if (string.IsNullOrWhiteSpace(keeper.DisplayName))
        {
            keeper.DisplayName = keeper.Name;
        }

        if (string.IsNullOrEmpty(keeper.PreviewImage))
        {
            var firstCover = Directory.Exists(coversDir)
                ? Directory.GetFiles(coversDir).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
                : null;
            keeper.PreviewImage = firstCover ?? "";
        }

        ModRepository.Save(game, keeper);

        var defaultGroup = groups.First(item => item.IsDefault);
        var fallbackIndex = mods.Count(item => item.GroupId == defaultGroup.Id);
        keeper.GroupId = defaultGroup.Id;
        keeper.Index = ++fallbackIndex;
        ModRepository.Save(game, keeper);

        foreach (var member in members.Where(item => item != keeper))
        {
            mods.Remove(member);
            ModRepository.Delete(game, member);
        }

        groups.Remove(group);
    }
}
