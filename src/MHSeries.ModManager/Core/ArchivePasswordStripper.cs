using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;

namespace MhModManager.Core;

/// <summary>
/// 把带密码的存储源压缩包重建为无密码 zip:SharpCompress 只能写 zip,产物固定为 <c>&lt;原文件名&gt;.zip</c>。
/// 嵌套加密包会递归重建为同名 zip,未加密的嵌套包保持字节不变;保证后续原地更新不再需要密码。
/// </summary>
public static class ArchivePasswordStripper
{
    /// <summary>返回去密码后的新压缩包路径;整棵树(含嵌套压缩包)都无加密时返回 null,原文件保持不动。</summary>
    public static string? Strip(string archivePath, IReadOnlyList<string> passwords)
    {
        if (!ArchiveExtractor.IsArchive(archivePath) || !File.Exists(archivePath))
        {
            return null;
        }

        AppPaths.EnsureCreated();
        var staging = Path.Combine(AppPaths.TempDir, Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            if (ArchiveExtractor.IsEncrypted(archivePath))
            {
                ArchiveExtractor.Extract(archivePath, staging, null, passwords);
                StripNested(staging, passwords);
            }
            else
            {
                // 顶层无密码时仍可能嵌套加密包,解包检查后按需重建。
                ArchiveExtractor.Extract(archivePath, staging);
                if (!StripNested(staging, passwords))
                {
                    return null;
                }
            }

            return RepackAsZip(staging, archivePath);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    private static bool StripNested(string staging, IReadOnlyList<string> passwords)
    {
        var replaced = false;
        foreach (var file in Directory.GetFiles(staging, "*.*", SearchOption.AllDirectories))
        {
            if (!ArchiveExtractor.IsArchive(file))
            {
                continue;
            }

            if (Strip(file, passwords) is not null)
            {
                replaced = true;
            }
        }

        return replaced;
    }

    private static string RepackAsZip(string staging, string originalPath)
    {
        var dest = Path.ChangeExtension(originalPath, ".zip");
        var sameTarget = Path.GetFullPath(dest).Equals(Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase);
        if (!sameTarget && File.Exists(dest))
        {
            File.Move(dest, dest + ".bak", true);
        }

        var temp = Path.Combine(AppPaths.TempDir, Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var stream = File.Create(temp))
            using (var writer = WriterFactory.Open(stream, ArchiveType.Zip, new ZipWriterOptions(CompressionType.Deflate)))
            {
                foreach (var file in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
                {
                    var entryPath = Path.GetRelativePath(staging, file).Replace('\\', '/');
                    using var source = File.OpenRead(file);
                    writer.Write(entryPath, source, File.GetLastWriteTime(file));
                }
            }

            File.Move(temp, dest, true);
        }
        finally
        {
            TryDeleteFile(temp);
        }

        if (!sameTarget)
        {
            File.Delete(originalPath);
        }

        return dest;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
