namespace MhModManager.Models;

public sealed class ParsedMod
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string StagingDir { get; set; } = "";
    public string PreviewSource { get; set; } = "";
    public int NexusId { get; set; }
    public string HomeUrl { get; set; } = "";
    public string Category { get; set; } = "";
    /// <summary>modinfo.ini 的 NameAsBundle 键：组件化导入时作为整个 mod 的显示名。</summary>
    public string BundleName { get; set; } = "";
    public List<ParsedFile> Files { get; set; } = [];
}

public sealed class ParsedFile
{
    public string SourcePath { get; set; } = "";
    public string RelativeDest { get; set; } = "";
}

public sealed class ImportBatch
{
    public List<ModRecord> Mods { get; set; } = [];
    public ModGroup? Group { get; set; }
}
