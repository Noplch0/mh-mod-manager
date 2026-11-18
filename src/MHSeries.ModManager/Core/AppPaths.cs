namespace MhModManager.Core;

public static class AppPaths
{
    private static string? _root;

    public static string Root => _root ??= Path.Combine(AppContext.BaseDirectory, "data");

    public static void Configure(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        _root = Path.GetFullPath(root);
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string TempDir => Path.Combine(Root, "temp");

    public static string DownloadDir => Path.Combine(Root, "downloads");

    /// <summary>某游戏存放源压缩包的下载目录（游戏间 MOD Id 会重复，必须按 appId 分开）。</summary>
    public static string DownloadGameDir(int steamAppId) => Path.Combine(DownloadDir, steamAppId.ToString());

    /// <summary>单个 MOD 的源压缩包目录：同名 MOD 互不覆盖，更新时整目录替换。</summary>
    public static string ModDownloadDir(int steamAppId, int modId) =>
        Path.Combine(DownloadGameDir(steamAppId), modId.ToString());

    public static string GameDir(int steamAppId) => Path.Combine(Root, "games", steamAppId.ToString());

    public static string ModsDir(int steamAppId) => Path.Combine(GameDir(steamAppId), "mods");

    public static string ModDir(int steamAppId, int id) => Path.Combine(ModsDir(steamAppId), id.ToString());

    public static string ModFilesDir(int steamAppId, int id) => Path.Combine(ModDir(steamAppId, id), "files");

    public static string GroupsFile(int steamAppId) => Path.Combine(GameDir(steamAppId), "groups.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(TempDir);
        Directory.CreateDirectory(DownloadDir);
    }
}
