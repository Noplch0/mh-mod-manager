namespace MhModManager.Models;

public sealed class AppSettings
{
    public GameId LastGame { get; set; } = GameId.Wilds;
    public bool CheckGameRunning { get; set; } = true;
    public int InstallOption { get; set; }

    /// <summary>Nexus Mods 官方 API Key(用户自行生成,明文存于本地 settings.json)。</summary>
    public string NexusApiKey { get; set; } = "";
    public Dictionary<int, string> GamePaths { get; set; } = [];
}
