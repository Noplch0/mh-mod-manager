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

    public string DeployPath(string relative)
    {
        var key = relative.Replace('\\', '/');
        return OverwriteFiles.TryGetValue(key, out var mapped) ? mapped : relative;
    }
}
