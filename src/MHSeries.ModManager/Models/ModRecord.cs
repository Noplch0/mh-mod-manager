using System.Text.Json.Serialization;

namespace MhModManager.Models;

public sealed class ModRecord
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int NexusId { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; }
    public int Index { get; set; }
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTimeOffset InstallTime { get; set; } = DateTimeOffset.Now;
    public string SourceFile { get; set; } = "";
    public string HomeUrl { get; set; } = "";
    public string PreviewImage { get; set; } = "";
    public List<string> Files { get; set; } = [];
    public Dictionary<string, string> OverwriteFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ModComponent> Components { get; set; } = [];

    /// <summary>组件化 MOD：一个记录内含多个可独立开关的组件。</summary>
    [JsonIgnore]
    public bool IsBundle => Components.Count > 0;

    /// <summary>
    /// 存储相对路径 → 游戏目录相对路径。pak_mods 映射优先；
    /// 组件化记录的存储路径带组件前缀（c{n}/...），部署时剥掉。
    /// </summary>
    public string DeployPath(string relative)
    {
        var key = relative.Replace('\\', '/');
        if (OverwriteFiles.TryGetValue(key, out var mapped))
        {
            return mapped;
        }

        return IsBundle ? ContentRelative(key) : key;
    }

    /// <summary>剥掉组件前缀后的部署相对路径；非组件化记录原样返回。</summary>
    public string ContentRelative(string stored)
    {
        if (!IsBundle)
        {
            return stored.Replace('\\', '/');
        }

        var key = stored.Replace('\\', '/');
        var separator = key.IndexOf('/');
        return separator > 0 ? key[(separator + 1)..] : key;
    }
}
