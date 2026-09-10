namespace MhModManager.Models;

public sealed class ModGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Index { get; set; }
    public bool IsDefault { get; set; }
    public bool Collapsed { get; set; }
}
