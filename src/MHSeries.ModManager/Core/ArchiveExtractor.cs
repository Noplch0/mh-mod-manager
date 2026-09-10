using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MhModManager.Core;

public static class ArchiveExtractor
{
    public static readonly string[] ArchiveExtensions = [".zip", ".7z", ".rar"];

    public static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path);
        return ArchiveExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    public static void Extract(string archivePath, string destDir, IProgress<int>? progress = null)
    {
        Directory.CreateDirectory(destDir);
        using var archive = ArchiveFactory.Open(archivePath);
        var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
        var total = Math.Max(entries.Count, 1);
        var index = 0;

        foreach (var entry in entries)
        {
            var relative = NormalizeEntryPath(entry.Key);
            if (string.IsNullOrWhiteSpace(relative))
            {
                continue;
            }

            if (!TryGetSafeDestination(destDir, relative, out var dest))
            {
                throw new InvalidDataException($"压缩包包含非法路径: {entry.Key}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.WriteToFile(dest, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
            index++;
            progress?.Report(index * 100 / total);
        }
    }

    public static void ExtractNested(string root)
    {
        foreach (var file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (!IsArchive(file))
            {
                continue;
            }

            var dest = file + "files";
            try
            {
                Extract(file, dest);
                File.Delete(file);
                ExtractNested(dest);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"无法解压嵌套压缩包: {Path.GetFileName(file)}", ex);
            }
        }
    }

    public static string UnwrapRoot(string dir)
    {
        var current = dir;
        for (var i = 0; i < 6; i++)
        {
            var files = Directory.GetFiles(current);
            var dirs = Directory.GetDirectories(current);
            var onlyDirName = dirs.Length == 1 ? Path.GetFileName(dirs[0]) : "";
            var isDeployRoot = onlyDirName.Equals("nativePC", StringComparison.OrdinalIgnoreCase)
                               || onlyDirName.Equals("natives", StringComparison.OrdinalIgnoreCase)
                               || onlyDirName.Equals("reframework", StringComparison.OrdinalIgnoreCase)
                               || onlyDirName.Equals("autorun", StringComparison.OrdinalIgnoreCase)
                               || onlyDirName.Equals("plugins", StringComparison.OrdinalIgnoreCase)
                               || onlyDirName.Equals("pl", StringComparison.OrdinalIgnoreCase)
                               || onlyDirName.Equals("wp", StringComparison.OrdinalIgnoreCase)
                               || onlyDirName.Equals("weapon", StringComparison.OrdinalIgnoreCase)
                               || onlyDirName.Equals("player", StringComparison.OrdinalIgnoreCase)
                               || onlyDirName.Equals("art", StringComparison.OrdinalIgnoreCase)
                               || onlyDirName.Equals("gamedesign", StringComparison.OrdinalIgnoreCase);
            if (files.Length == 0 && dirs.Length == 1 && !isDeployRoot)
            {
                current = dirs[0];
                continue;
            }

            break;
        }

        return current;
    }

    private static bool TryGetSafeDestination(string root, string relative, out string destination)
    {
        destination = "";
        if (relative.Contains(':') || Path.IsPathRooted(relative))
        {
            return false;
        }

        var rootFull = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        destination = candidate;
        return true;
    }

    private static string NormalizeEntryPath(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "";
        }

        return key.Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
