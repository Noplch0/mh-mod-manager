using MhModManager.Models;

namespace MhModManager.Core.Equipment;

/// <summary>
/// 原版 ChangeModelTask：禁用状态下把 MOD 文件（含 PAK 内部路径）改写到另一套装备。
/// </summary>
internal static class EquipmentRemapper
{
    public static int Apply(
        GameId game,
        string filesDir,
        IEnumerable<string> relativeFiles,
        EquipKind kind,
        int fromId,
        int toId,
        bool isPfb,
        bool withTex)
    {
        if (!Directory.Exists(filesDir))
        {
            return 0;
        }

        var changed = 0;
        var moves = new List<(string Source, string Dest)>();
        foreach (var relative in relativeFiles)
        {
            var source = Path.Combine(filesDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
            {
                continue;
            }

            if (IsPakPatch(relative) && game != GameId.World)
            {
                changed += PakReader.RewritePaths(source, PakFileIndex.For(game), path =>
                    MapInternal(game, kind, fromId, toId, isPfb, withTex, path));
                continue;
            }

            if (!ShouldRewrite(relative, isPfb, withTex))
            {
                continue;
            }

            if (!EquipmentPathRewriter.TryRewrite(game, kind, fromId, toId, relative, out var destRelative)
                || destRelative.Equals(relative, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dest = Path.Combine(filesDir, destRelative.Replace('/', Path.DirectorySeparatorChar));
            moves.Add((source, dest));
        }

        if (moves.Count > 0)
        {
            changed += MoveFiles(moves);
        }

        DeleteEmptyDirectories(filesDir);
        return changed;
    }

    private static string? MapInternal(
        GameId game,
        EquipKind kind,
        int fromId,
        int toId,
        bool isPfb,
        bool withTex,
        string path)
    {
        if (!ShouldRewrite(path, isPfb, withTex))
        {
            return null;
        }

        return EquipmentPathRewriter.TryRewrite(game, kind, fromId, toId, path, out var next)
            ? next
            : null;
    }

    private static bool ShouldRewrite(string path, bool isPfb, bool withTex)
    {
        var name = Path.GetFileName(path);
        var pfb = name.Contains(".pfb.", StringComparison.OrdinalIgnoreCase)
                  || name.EndsWith(".pfb", StringComparison.OrdinalIgnoreCase);
        if (pfb != isPfb)
        {
            return false;
        }

        var tex = name.Contains(".tex.", StringComparison.OrdinalIgnoreCase)
                  || name.EndsWith(".tex", StringComparison.OrdinalIgnoreCase);
        return withTex || !tex;
    }

    private static bool IsPakPatch(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && !normalized.Contains('/');
    }

    private static int MoveFiles(List<(string Source, string Dest)> moves)
    {
        var staged = new List<(string Temp, string Dest)>();
        try
        {
            foreach (var (source, dest) in moves)
            {
                if (source.Equals(dest, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var temp = source + ".hf-remap-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                File.Move(source, temp);
                staged.Add((temp, dest));
            }

            foreach (var (temp, dest) in staged)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest))
                {
                    File.Delete(dest);
                }

                File.Move(temp, dest);
            }

            return staged.Count;
        }
        catch
        {
            foreach (var (temp, dest) in staged)
            {
                try
                {
                    if (File.Exists(temp) && !File.Exists(dest))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                        File.Move(temp, dest);
                    }
                }
                catch
                {
                }
            }

            throw;
        }
    }

    private static void DeleteEmptyDirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(item => item.Length))
        {
            try
            {
                if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                {
                    Directory.Delete(dir);
                }
            }
            catch
            {
            }
        }
    }
}
