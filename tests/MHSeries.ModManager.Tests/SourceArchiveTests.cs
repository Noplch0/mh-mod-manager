using System.IO.Compression;
using MhModManager.Core;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

/// <summary>
/// 安装源压缩包的归档位置：每个 MOD 独占一个目录，同名 MOD 不互相覆盖，
/// 更新只保留最新副本（不再生成 .bak），把已存的包再当更新源也不会被清理掉。
/// </summary>
public sealed class SourceArchiveTests : IDisposable
{
    private readonly int _appId = Random.Shared.Next(9_000_001, 9_900_000);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mh-mod-manager-source-tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _settings = new();
    private readonly GameProfile _game;
    private readonly ModService _service;

    public SourceArchiveTests()
    {
        _game = new GameProfile(GameId.World, "source-test", "test", "test", "test.exe", _appId, "test", false);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, _game.ExeName), "fake-game");
        _settings.Load();
        _settings.Current.CheckGameRunning = false;
        _settings.SetGamePath(_game, _root);
        _service = new ModService(_settings);
    }

    [Fact]
    public void SameDisplayNameModsKeepSeparateStoredSources()
    {
        _settings.Current.InstallOption = 1;
        var first = CreateZip("first", "same-name.zip", "first-content");
        var second = CreateZip("second", "same-name.zip", "second-content");
        var firstBytes = File.ReadAllBytes(first);
        var secondBytes = File.ReadAllBytes(second);

        var firstMod = _service.Import(_game, first).Mods[0];
        var secondMod = _service.Import(_game, second).Mods[0];

        Assert.Equal(firstMod.DisplayName, secondMod.DisplayName);
        Assert.NotEqual(firstMod.SourceFile, secondMod.SourceFile);
        Assert.Equal(AppPaths.ModDownloadDir(_appId, firstMod.Id), Path.GetDirectoryName(firstMod.SourceFile));
        Assert.Equal(AppPaths.ModDownloadDir(_appId, secondMod.Id), Path.GetDirectoryName(secondMod.SourceFile));
        Assert.Equal(firstBytes, File.ReadAllBytes(firstMod.SourceFile));
        Assert.Equal(secondBytes, File.ReadAllBytes(secondMod.SourceFile));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(firstMod.SourceFile)!));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(secondMod.SourceFile)!));
    }

    [Fact]
    public void UpdatingSourceReplacesOldCopyWithoutBackup()
    {
        _settings.Current.InstallOption = 1;
        var mod = _service.Import(_game, CreateZip("v1", "pkg.zip", "one")).Mods[0];
        var oldStored = mod.SourceFile;

        var updated = _service.Update(_game, mod, CreateZip("v2", "pkg-v2.zip", "two")).Mods[0];

        var dir = AppPaths.ModDownloadDir(_appId, updated.Id);
        Assert.Equal(dir, Path.GetDirectoryName(updated.SourceFile));
        Assert.NotEqual(oldStored, updated.SourceFile);
        Assert.False(File.Exists(oldStored));
        Assert.Single(Directory.GetFiles(dir));
        Assert.Empty(Directory.GetFiles(dir, "*.bak"));
    }

    [Fact]
    public void ReusingStoredArchiveAsUpdateSourceKeepsIt()
    {
        _settings.Current.InstallOption = 2;
        var mod = _service.Import(_game, CreateZip("base", "base.zip", "payload")).Mods[0];
        var stored = mod.SourceFile;
        Assert.True(File.Exists(stored));

        var updated = _service.Update(_game, mod, stored).Mods[0];

        Assert.Equal(stored, updated.SourceFile);
        Assert.True(File.Exists(stored));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(stored)!));
    }

    private string CreateZip(string folder, string fileName, string content)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        using var entry = zip.CreateEntry("nativePC/hello.bin").Open();
        using var writer = new StreamWriter(entry);
        writer.Write(content);
        return path;
    }

    public void Dispose()
    {
        _settings.Current.GamePaths.Remove(_appId);
        _settings.Current.InstallOption = 0;
        _settings.Save();

        if (Directory.Exists(AppPaths.GameDir(_appId)))
        {
            Directory.Delete(AppPaths.GameDir(_appId), true);
        }

        if (Directory.Exists(AppPaths.DownloadGameDir(_appId)))
        {
            Directory.Delete(AppPaths.DownloadGameDir(_appId), true);
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
