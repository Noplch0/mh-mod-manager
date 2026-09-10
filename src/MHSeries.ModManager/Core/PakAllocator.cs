using MhModManager.Models;

namespace MhModManager.Core;

public static class PakAllocator
{
    public static void Assign(GameProfile game, string gamePath, IEnumerable<ModRecord> mods, ModRecord target, bool enabling)
    {
        if (!game.UsesPakPatches)
        {
            return;
        }

        var targetNames = GetPakFiles(target, useOverwrite: true)
            .Select(file => Path.GetFileName(target.DeployPath(file)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<int>(ExistingPatchNumbers(game, gamePath, targetNames));

        foreach (var mod in mods)
        {
            if (mod == target || !mod.Enabled)
            {
                continue;
            }

            foreach (var file in GetPakFiles(mod, useOverwrite: true))
            {
                var number = ModLayoutParser.ParsePakNumber(mod.DeployPath(file));
                if (number > 0)
                {
                    used.Add(number);
                }
            }
        }

        var next = used.Count > 0 ? used.Max() + 1 : 1;
        foreach (var file in GetPakFiles(target, useOverwrite: false))
        {
            if (!enabling)
            {
                target.OverwriteFiles.Remove(Normalize(file));
                continue;
            }

            while (used.Contains(next))
            {
                next++;
            }

            if (next > 999)
            {
                throw new InvalidOperationException("PAK 补丁编号已用尽（最大 999）");
            }

            var mapped = ModLayoutParser.FormatPakName(game.PakPrefix, next);
            target.OverwriteFiles[Normalize(file)] = mapped;
            used.Add(next);
            next++;
        }
    }

    public static void Repair(GameProfile game, string gamePath, IEnumerable<ModRecord> mods)
    {
        if (!game.UsesPakPatches || !Directory.Exists(gamePath))
        {
            return;
        }

        var existing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(gamePath, "*.pak"))
        {
            if (!IsPatchPak(game, Path.GetFileName(file)))
            {
                continue;
            }

            var number = ModLayoutParser.ParsePakNumber(file);
            if (number >= 1)
            {
                existing[Md5Prefix(file)] = number;
            }
        }

        foreach (var mod in mods.Where(m => m.Enabled))
        {
            var changed = false;
            foreach (var file in GetPakFiles(mod, useOverwrite: false))
            {
                var source = Path.Combine(AppPaths.ModFilesDir(game.SteamAppId, mod.Id), file.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(source))
                {
                    continue;
                }

                var hash = Md5Prefix(source);
                if (existing.TryGetValue(hash, out var number))
                {
                    var mapped = ModLayoutParser.FormatPakName(game.PakPrefix, number);
                    if (!string.Equals(mod.DeployPath(file), mapped, StringComparison.OrdinalIgnoreCase))
                    {
                        mod.OverwriteFiles[Normalize(file)] = mapped;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                ModRepository.Save(game, mod);
            }
        }
    }

    public static IEnumerable<string> GetPakFiles(ModRecord mod, bool useOverwrite)
    {
        foreach (var file in mod.Files)
        {
            var dest = useOverwrite ? mod.DeployPath(file) : file;
            if (ModLayoutParser.IsPakFile(dest) || ModLayoutParser.IsPakFile(file))
            {
                yield return file;
            }
        }
    }

    public static void Clear(ModRecord mod) => mod.OverwriteFiles.Clear();

    public static IEnumerable<string> FindMatchingPatches(GameProfile game, string gamePath, string sourcePak)
    {
        if (!game.UsesPakPatches || !Directory.Exists(gamePath) || !File.Exists(sourcePak))
        {
            yield break;
        }

        var hash = Md5Prefix(sourcePak);
        foreach (var file in Directory.GetFiles(gamePath, "*.pak"))
        {
            var name = Path.GetFileName(file);
            if (!IsPatchPak(game, name))
            {
                continue;
            }

            if (Md5Prefix(file) == hash)
            {
                yield return name;
            }
        }
    }

    private static IEnumerable<int> ExistingPatchNumbers(GameProfile game, string gamePath, HashSet<string> ignoreNames)
    {
        if (!Directory.Exists(gamePath))
        {
            yield break;
        }

        foreach (var file in Directory.GetFiles(gamePath, "*.pak"))
        {
            var name = Path.GetFileName(file);
            if (ignoreNames.Contains(name) || !IsPatchPak(game, name))
            {
                continue;
            }

            var number = ModLayoutParser.ParsePakNumber(name);
            if (number > 0)
            {
                yield return number;
            }
        }
    }

    private static bool IsPatchPak(GameProfile game, string fileName) =>
        fileName.Contains(".patch_", StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrEmpty(game.PakPrefix)
            && fileName.StartsWith(game.PakPrefix, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string Md5Prefix(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(stream.Length, 1024 * 1024)];
        var read = stream.Read(buffer, 0, buffer.Length);
        var hash = System.Security.Cryptography.MD5.HashData(buffer.AsSpan(0, read));
        return Convert.ToHexString(hash) + ":" + new FileInfo(path).Length;
    }
}
