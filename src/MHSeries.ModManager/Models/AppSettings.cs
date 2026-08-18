namespace HuntForge.Models;

public sealed class AppSettings
{
    public GameId LastGame { get; set; } = GameId.Wilds;
    public bool CheckGameRunning { get; set; } = true;
    public bool FixPakNumber { get; set; } = true;
    public int InstallOption { get; set; }
    public Dictionary<int, string> GamePaths { get; set; } = [];
}
