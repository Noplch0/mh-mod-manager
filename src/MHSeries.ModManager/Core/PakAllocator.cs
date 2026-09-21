using MhModManager.Models;

namespace MhModManager.Core;

public static class PakAllocator
{
    /// <summary>存储路径是否为 pak（组件化记录按剥前缀后的部署路径判断）。</summary>
    public static bool IsPakStored(ModRecord mod, string stored) =>
        ModLayoutParser.IsPakFile(mod.ContentRelative(stored));

    /// <summary>记录内全部 pak 的存储路径（不含启用状态过滤）。</summary>
    public static IEnumerable<string> GetPakFilesAll(ModRecord mod) =>
        mod.Files.Where(file => IsPakStored(mod, file));

    /// <summary>部署视角的 pak：组件化记录只含启用组件的 pak（按组件顺序）。</summary>
    public static IEnumerable<string> GetPakFiles(ModRecord mod, bool useOverwrite)
    {
        if (mod.IsBundle)
        {
            foreach (var file in mod.Components
                         .Where(component => component.Enabled)
                         .SelectMany(component => component.Files)
                         .Where(file => IsPakStored(mod, file)))
            {
                yield return file;
            }

            yield break;
        }

        foreach (var file in mod.Files)
        {
            var dest = useOverwrite ? mod.DeployPath(file) : file;
            if (ModLayoutParser.IsPakFile(dest) || ModLayoutParser.IsPakFile(file))
            {
                yield return file;
            }
        }
    }

    internal static string PrefixHash(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(stream.Length, 1024 * 1024)];
        var read = stream.Read(buffer, 0, buffer.Length);
        var hash = System.Security.Cryptography.MD5.HashData(buffer.AsSpan(0, read));
        return Convert.ToHexString(hash) + ":" + new FileInfo(path).Length;
    }
}
