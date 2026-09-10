using System.IO.Compression;
using System.Reflection;
using System.Text;
using MhModManager.Models;

namespace MhModManager.Core.Equipment;

/// <summary>
/// 游戏完整文件清单（MHWs.list / mhrisePC.list，gzip 内嵌）。
/// 把 pak 条目的 64 位路径 hash 映射回游戏内路径，从而解析 PAK 补丁里的模型文件。
/// </summary>
internal sealed class PakFileIndex
{
    private static readonly Dictionary<GameId, PakFileIndex> Cache = [];
    private readonly Dictionary<ulong, string> _paths = [];
    private readonly Dictionary<ulong, string> _raw = [];

    public static PakFileIndex For(GameId game)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(game, out var index))
            {
                index = new PakFileIndex(game);
                Cache[game] = index;
            }

            return index;
        }
    }

    private PakFileIndex(GameId game)
    {
        var resource = game switch
        {
            GameId.World => "",
            GameId.Rise => "MhModManager.Data.Equipment.mhrisePC.list.txt.gz",
            _ => "MhModManager.Data.Equipment.mhws.list.txt.gz"
        };
        if (resource.Length == 0)
        {
            return;
        }

        var text = ReadResource(resource);
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var hash = PakHash.PathHash(line);
            _raw[hash] = line;
            _paths[hash] = line.Replace('\\', '/');
        }
    }

    public string? GetPath(ulong hash) =>
        _paths.TryGetValue(hash, out var path) ? path : null;

    public string? GetRawPath(ulong hash) =>
        _raw.TryGetValue(hash, out var path) ? path : null;

    public int Count => _paths.Count;

    private static string ReadResource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"缺少文件清单资源: {name}");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
