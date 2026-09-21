using System.Security.Cryptography;
using MhModManager.Models;

namespace MhModManager.Core;

/// <summary>
/// pak_mods 部署：pak 统一部署到游戏根目录 pak_mods，
/// 文件名 X{index:0004}-{MOD名}.pak，序号跟随管理器列表顺序（分组顺序 + 组内顺序）。
/// </summary>
public static class PakModsManager
{
    public const string DirName = "pak_mods";
    private const string Prefix = "X";

    public static string PakModsDir(string gamePath) => Path.Combine(gamePath, DirName);

    /// <summary>单个 MOD 的 pak 期望部署名（不含切换同步）。</summary>
    public static string FormatName(int index, ModRecord mod, int pakOrdinal, int pakCount)
    {
        var suffix = pakCount > 1 ? $"-{pakOrdinal + 1}" : "";
        return $"{Prefix}{index.ToString("D4")}-{SafeName(mod)}{suffix}.pak";
    }

    /// <summary>是否为管理器托管的 pak_mods 部署路径。</summary>
    public static bool IsManagedTarget(string destRelative)
    {
        var path = destRelative.Replace('\\', '/');
        if (!path.StartsWith(DirName + "/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = Path.GetFileName(path);
        return name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
               && name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>让磁盘文件与映射跟当前启用列表、名称保持一致。幂等。</summary>
    public static void Sync(GameProfile game, string gamePath, List<ModRecord> mods, IReadOnlyList<ModGroup> groups)
    {
        if (!game.UsesPakPatches || !Directory.Exists(gamePath))
        {
            return;
        }

        var dir = PakModsDir(gamePath);
        Directory.CreateDirectory(dir);

        // 期望映射：GroupOrder 排序下的已启用 MOD，序号 = 在启用列表中的位置。
        var ordered = GroupOrder.Ordered(mods.Where(mod => mod.Enabled), groups).ToList();
        var desired = new List<(ModRecord Mod, string Stored, string Target)>();
        foreach (var mod in ordered)
        {
            var paks = PakAllocator.GetPakFiles(mod, useOverwrite: false).ToList();
            for (var i = 0; i < paks.Count; i++)
            {
                var target = $"{DirName}/{FormatName(ordered.IndexOf(mod), mod, i, paks.Count)}";
                desired.Add((mod, Normalize(paks[i]), target));
            }
        }

        // 1) 重新映射：磁盘旧文件改名到新目标，映射写入 OverwriteFiles。
        //    旧版部署在游戏根目录的编号 pak 也有映射记录，会一并移入 pak_mods。
        var changedMods = new HashSet<ModRecord>();
        foreach (var group in desired.GroupBy(item => item.Mod))
        {
            foreach (var (_, stored, target) in group)
            {
                var oldRelative = group.Key.DeployPath(stored);
                if (string.Equals(oldRelative, target, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var source = Path.Combine(gamePath, oldRelative.Replace('/', Path.DirectorySeparatorChar));
                var dest = Path.Combine(gamePath, target.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(source))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    if (!Path.GetFullPath(source).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(dest))
                        {
                            File.Delete(dest);
                        }

                        File.Move(source, dest);
                    }
                }

                group.Key.OverwriteFiles[stored] = target;
                changedMods.Add(group.Key);
            }
        }

        foreach (var mod in changedMods)
        {
            ModRepository.Save(game, mod);
        }

        // 2) 清理孤儿：目录里 X 前缀、哈希匹配托管 pak、但已不在期望集合的文件。
        var desiredFiles = desired.Select(item => Path.GetFileName(item.Target))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var managedHashes = CollectManagedHashes(game, mods);
        foreach (var file in Directory.GetFiles(dir, "*.pak"))
        {
            var name = Path.GetFileName(file);
            if (desiredFiles.Contains(name) || !name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (managedHashes.Contains(HashPrefix(file)))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>启用 MOD 后按列表顺序重排全部 X 编号。</summary>
    public static void AssignForEnable(GameProfile game, string gamePath, List<ModRecord> mods, IReadOnlyList<ModGroup> groups)
    {
        if (!game.UsesPakPatches)
        {
            return;
        }

        Sync(game, gamePath, mods, groups);
    }

    internal static string SafeName(ModRecord mod)
    {
        var name = string.IsNullOrWhiteSpace(mod.DisplayName) ? mod.Name : mod.DisplayName;
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "mod" : safe;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static HashSet<string> CollectManagedHashes(GameProfile game, IEnumerable<ModRecord> mods)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            foreach (var file in PakAllocator.GetPakFilesAll(mod))
            {
                var source = Path.Combine(AppPaths.ModFilesDir(game.SteamAppId, mod.Id), file.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(source))
                {
                    hashes.Add(HashPrefix(source));
                }
            }
        }

        return hashes;
    }

    private static string HashPrefix(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(stream.Length, 1024 * 1024)];
        var read = stream.Read(buffer, 0, buffer.Length);
        var hash = MD5.HashData(buffer.AsSpan(0, read));
        return Convert.ToHexString(hash) + ":" + new FileInfo(path).Length;
    }
}
