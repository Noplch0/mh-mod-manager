using System.Runtime.ExceptionServices;
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

    /// <summary>不解压数据,仅探测压缩包内是否存在加密条目(ZipCrypto 在无密码时枚举即抛 CryptographicException,同样视为加密)。</summary>
    public static bool IsEncrypted(string archivePath)
    {
        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            return archive.Entries.Any(entry => entry.IsEncrypted && !entry.IsDirectory);
        }
        catch (Exception ex) when (ex is CryptographicException)
        {
            return true;
        }
    }

    /// <summary>passwords 为候选密码列表:遇到加密包时逐个尝试,全部失败抛 PasswordRequiredException。</summary>
    public static void Extract(string archivePath, string destDir, IProgress<int>? progress = null, IReadOnlyList<string>? passwords = null)
    {
        Directory.CreateDirectory(destDir);
        if (!IsEncrypted(archivePath))
        {
            ExtractEntries(archivePath, destDir, progress, null);
            return;
        }

        var candidates = (passwords ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var name = Path.GetFileName(archivePath);
        if (candidates.Count == 0)
        {
            throw new PasswordRequiredException(name, $"压缩包 {name} 已加密，需要提供密码");
        }

        Exception? lastError = null;
        foreach (var password in candidates)
        {
            try
            {
                ExtractEntries(archivePath, destDir, progress, password);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (IsPasswordFailure(lastError!))
        {
            throw new PasswordRequiredException(name, $"密码不正确或压缩包已损坏: {name}", lastError);
        }

        ExceptionDispatchInfo.Capture(lastError!).Throw();
    }

    public static void ExtractNested(string root, IReadOnlyList<string>? passwords = null)
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
                Extract(file, dest, null, passwords);
                File.Delete(file);
                ExtractNested(dest, passwords);
            }
            catch (Exception ex) when (ex is not PasswordRequiredException)
            {
                throw new InvalidDataException($"无法解压嵌套压缩包: {Path.GetFileName(file)}", ex);
            }
        }
    }

    private static void ExtractEntries(string archivePath, string destDir, IProgress<int>? progress, string? password)
    {
        using var archive = string.IsNullOrEmpty(password)
            ? ArchiveFactory.Open(archivePath)
            : ArchiveFactory.Open(archivePath, new ReaderOptions { Password = password });
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

    private static bool IsPasswordFailure(Exception ex) =>
        ex is CryptographicException or InvalidFormatException
        || ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase);

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
