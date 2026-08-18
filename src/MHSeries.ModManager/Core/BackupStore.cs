using HuntForge.Models;

namespace HuntForge.Core;

public sealed class BackupStore
{
    private readonly GameProfile _game;
    private Dictionary<string, string> _hashes = new(StringComparer.OrdinalIgnoreCase);

    public BackupStore(GameProfile game)
    {
        _game = game;
        Load();
    }

    public void OnDeploy(string gamePath, string relative, string destPath)
    {
        if (!ModLayoutParser.CanBackup(_game, relative) || !File.Exists(destPath))
        {
            return;
        }

        var key = relative.Replace('\\', '/');
        var backup = Path.Combine(AppPaths.BackupDir(_game.SteamAppId), relative);
        if (_hashes.ContainsKey(key) && File.Exists(backup))
        {
            return;
        }

        var hash = FileHash(destPath);
        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(destPath, backup, true);
        _hashes[key] = hash;
        Save();
    }

    public void OnRemove(string destPath, string relative, bool keepBackup)
    {
        if (!ModLayoutParser.CanBackup(_game, relative))
        {
            return;
        }

        var key = relative.Replace('\\', '/');
        var backup = Path.Combine(AppPaths.BackupDir(_game.SteamAppId), relative);
        if (!File.Exists(backup))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(backup, destPath, true);
        if (!keepBackup)
        {
            File.Delete(backup);
            _hashes.Remove(key);
            Save();
        }
    }

    private void Load()
    {
        var path = Path.Combine(AppPaths.BackupDir(_game.SteamAppId), "filelist.json");
        _hashes = JsonUtil.Load(path, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private void Save()
    {
        JsonUtil.Save(Path.Combine(AppPaths.BackupDir(_game.SteamAppId), "filelist.json"), _hashes);
    }

    private static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = System.Security.Cryptography.MD5.HashData(stream);
        return $"{stream.Length}:{Convert.ToHexString(hash)}";
    }
}
