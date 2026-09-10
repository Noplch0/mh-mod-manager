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

    public static string GameDir(int steamAppId) => Path.Combine(Root, "games", steamAppId.ToString());

    public static string ModsDir(int steamAppId) => Path.Combine(GameDir(steamAppId), "mods");

    public static string ModDir(int steamAppId, int id) => Path.Combine(ModsDir(steamAppId), id.ToString());

    public static string ModFilesDir(int steamAppId, int id) => Path.Combine(ModDir(steamAppId, id), "files");

    public static string BackupDir(int steamAppId) => Path.Combine(GameDir(steamAppId), "backups");

    public static string GroupsFile(int steamAppId) => Path.Combine(GameDir(steamAppId), "groups.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(TempDir);
        Directory.CreateDirectory(DownloadDir);
    }
}
