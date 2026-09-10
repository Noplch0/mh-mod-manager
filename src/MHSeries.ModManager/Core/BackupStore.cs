using MhModManager.Models;

namespace MhModManager.Core;

public sealed class BackupStore
{
    private readonly GameProfile _game;
    private Dictionary<string, string> _hashes = new(StringComparer.OrdinalIgnoreCase);

    public BackupStore(GameProfile game)
    {
        _game = game;
        Load();
    }

    public void OnDeploy(string relative, string destPath, string sourcePath)
    {
        if (!ModLayoutParser.CanBackup(_game, relative) || !File.Exists(destPath))
        {
            return;
        }

        // 游戏目录里已经是这份 MOD 时不要当“原文件”备份，否则关闭时会把 MOD 再拷回去。
        if (File.Exists(sourcePath) && FilesEqual(destPath, sourcePath))
        {
            return;
        }

        var key = Normalize(relative);
        var backup = BackupPath(relative);
        if (_hashes.ContainsKey(key) && File.Exists(backup))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
        File.Copy(destPath, backup, true);
        _hashes[key] = FileHash(destPath);
        Save();
    }

    public void OnRemove(string destPath, string relative, bool keepBackup, string? sourcePath)
    {
        if (!ModLayoutParser.CanBackup(_game, relative))
        {
            return;
        }

        var key = Normalize(relative);
        var backup = BackupPath(relative);
        if (!File.Exists(backup))
        {
            return;
        }

        // 备份其实是 MOD 自身（从原盒子迁过来、或上次误备份）时，保持删除，不要还原。
        if (sourcePath is not null && File.Exists(sourcePath) && FilesEqual(backup, sourcePath))
        {
            DiscardBackup(backup, key, keepBackup);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(backup, destPath, true);
        DiscardBackup(backup, key, keepBackup);
    }

    private void DiscardBackup(string backup, string key, bool keepBackup)
    {
        if (keepBackup)
        {
            return;
        }

        if (File.Exists(backup))
        {
            File.Delete(backup);
        }

        _hashes.Remove(key);
        Save();
    }

    private string BackupPath(string relative) =>
        Path.Combine(AppPaths.BackupDir(_game.SteamAppId), relative.Replace('/', Path.DirectorySeparatorChar));

    private void Load()
    {
        var path = Path.Combine(AppPaths.BackupDir(_game.SteamAppId), "filelist.json");
        _hashes = JsonUtil.Load(path, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private void Save()
    {
        JsonUtil.Save(Path.Combine(AppPaths.BackupDir(_game.SteamAppId), "filelist.json"), _hashes);
    }

    private static string Normalize(string relative) => relative.Replace('\\', '/');

    private static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = System.Security.Cryptography.MD5.HashData(stream);
        return $"{stream.Length}:{Convert.ToHexString(hash)}";
    }

    internal static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (!leftInfo.Exists || !rightInfo.Exists || leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        var leftBuffer = new byte[1024 * 1024];
        var rightBuffer = new byte[1024 * 1024];
        int leftRead;
        while ((leftRead = leftStream.Read(leftBuffer, 0, leftBuffer.Length)) > 0)
        {
            var rightRead = rightStream.Read(rightBuffer, 0, rightBuffer.Length);
            if (leftRead != rightRead || !leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
            {
                return false;
            }
        }

        return rightStream.ReadByte() == -1;
    }
}
