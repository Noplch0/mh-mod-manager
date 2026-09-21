using MhModManager.Core;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

public sealed class PakModsTests : IDisposable
{
    private readonly int _appId = Random.Shared.Next(9_000_001, 9_900_000);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mh-mod-manager-pakmods-tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _settings = new();
    private readonly GameProfile _game;
    private readonly ModService _service;

    public PakModsTests()
    {
        _game = new GameProfile(GameId.Rise, "rise-test", "rise", "rise", "rise.exe", _appId, "rise",
            "re_chunk_000.pak.patch_", 1);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, _game.ExeName), "fake-game");
        _settings.Load();
        _settings.Current.InstallOption = 0;
        _settings.Current.CheckGameRunning = false;
        _settings.SetGamePath(_game, _root);
        _service = new ModService(_settings);
    }

    [Fact]
    public void SinglePakGetsIndexPrefixAndDisplayName()
    {
        var mod = InstallPak("first", "旧文件名", "first-mod");
        _service.SetEnabled(_game, mod, true);

        var deployed = Path.Combine(_root, "pak_mods", "X0000-first-mod.pak");
        Assert.True(File.Exists(deployed));
        Assert.Equal("pak_mods/X0000-first-mod.pak", mod.DeployPath("old.pak"));
        Assert.False(File.Exists(Path.Combine(_root, "old.pak")));
    }

    [Fact]
    public void MultiplePaksInOneModGetOrdinalSuffixes()
    {
        var staging = CreateStaging("multi");
        var parsed = new ParsedMod
        {
            Name = "multi",
            SourceFile = Path.Combine(staging, "multi.zip"),
            StagingDir = staging,
            Files =
            [
                new ParsedFile { SourcePath = WriteFile(staging, "a.pak", "a"), RelativeDest = "a.pak" },
                new ParsedFile { SourcePath = WriteFile(staging, "b.pak", "b"), RelativeDest = "b.pak" }
            ]
        };
        var mod = _service.Install(_game, parsed);
        _service.SetEnabled(_game, mod, true);

        Assert.True(File.Exists(Path.Combine(_root, "pak_mods", "X0000-multi-1.pak")));
        Assert.True(File.Exists(Path.Combine(_root, "pak_mods", "X0000-multi-2.pak")));
    }

    [Fact]
    public void SecondEnabledModGetsNextIndex()
    {
        var first = InstallPak("first", "a", "first-mod");
        var second = InstallPak("second", "b", "second-mod");
        _service.SetEnabled(_game, first, true);
        _service.SetEnabled(_game, second, true);

        Assert.True(File.Exists(Path.Combine(_root, "pak_mods", "X0000-first-mod.pak")));
        Assert.True(File.Exists(Path.Combine(_root, "pak_mods", "X0001-second-mod.pak")));
    }

    [Fact]
    public void DisablingReindexesRemainingModsOnDisk()
    {
        var first = InstallPak("first", "a", "first-mod");
        var second = InstallPak("second", "b", "second-mod");
        _service.SetEnabled(_game, first, true);
        _service.SetEnabled(_game, second, true);

        _service.SetEnabled(_game, first, false);

        Assert.False(File.Exists(Path.Combine(_root, "pak_mods", "X0000-first-mod.pak")));
        Assert.True(File.Exists(Path.Combine(_root, "pak_mods", "X0000-second-mod.pak")));
        Assert.Equal("pak_mods/X0000-second-mod.pak", second.DeployPath("old.pak"));
    }

    [Fact]
    public void RenameSyncsDeployedFileName()
    {
        var mod = InstallPak("first", "a", "first-mod");
        _service.SetEnabled(_game, mod, true);
        Assert.True(File.Exists(Path.Combine(_root, "pak_mods", "X0000-first-mod.pak")));

        _service.RenameMod(_game, mod, "新名字");

        Assert.False(File.Exists(Path.Combine(_root, "pak_mods", "X0000-first-mod.pak")));
        Assert.True(File.Exists(Path.Combine(_root, "pak_mods", "X0000-新名字.pak")));
        Assert.Equal("pak_mods/X0000-新名字.pak", mod.DeployPath("old.pak"));
        Assert.Equal("a", File.ReadAllText(Path.Combine(_root, "pak_mods", "X0000-新名字.pak")));
    }

    [Fact]
    public void MovingModUpdatesIndexesOnDisk()
    {
        var first = InstallPak("first", "a", "first-mod");
        var second = InstallPak("second", "b", "second-mod");
        _service.SetEnabled(_game, first, true);
        _service.SetEnabled(_game, second, true);

        _service.Move(_game, second, -1);

        Assert.True(File.Exists(Path.Combine(_root, "pak_mods", "X0000-second-mod.pak")));
        Assert.True(File.Exists(Path.Combine(_root, "pak_mods", "X0001-first-mod.pak")));
    }

    [Fact]
    public void UninstallRemovesPakFromPakMods()
    {
        var mod = InstallPak("first", "a", "first-mod");
        _service.SetEnabled(_game, mod, true);
        Assert.True(File.Exists(Path.Combine(_root, "pak_mods", "X0000-first-mod.pak")));

        _service.Uninstall(_game, mod);

        Assert.False(File.Exists(Path.Combine(_root, "pak_mods", "X0000-first-mod.pak")));
    }

    [Fact]
    public void GameRootPaksAreNeverTouchedByDeployOrUndeploy()
    {
        var basePak = WriteFile(_root, "re_chunk_000.pak", "game-base");
        var officialPatch = WriteFile(_root, "re_chunk_000.pak.patch_001.pak", "game-patch");
        var mod = InstallPak("first", "a", "first-mod");

        _service.SetEnabled(_game, mod, true);
        Assert.Equal("game-base", File.ReadAllText(basePak));
        Assert.Equal("game-patch", File.ReadAllText(officialPatch));

        _service.SetEnabled(_game, mod, false);
        Assert.Equal("game-base", File.ReadAllText(basePak));
        Assert.Equal("game-patch", File.ReadAllText(officialPatch));
    }

    [Fact]
    public void UnsafeCharactersInDisplayNameAreSanitized()
    {
        var mod = InstallPak("first", "a", "first-mod");
        _service.SetEnabled(_game, mod, true);
        _service.RenameMod(_game, mod, "bad:name*?.pak");

        var deployed = Directory.GetFiles(Path.Combine(_root, "pak_mods"), "X0000-*.pak");
        var name = Assert.Single(deployed);
        Assert.DoesNotContain(":", Path.GetFileName(name));
        Assert.DoesNotContain("*", Path.GetFileName(name));
        Assert.DoesNotContain("?", Path.GetFileName(name));
    }

    [Fact]
    public void UserFilesInPakModsWithoutManagedHashAreKept()
    {
        var mod = InstallPak("first", "a", "first-mod");
        _service.SetEnabled(_game, mod, true);
        WriteFile(_root, "pak_mods/X0000-user-custom.pak", "user data");

        _service.RenameMod(_game, mod, "renamed");

        Assert.True(File.Exists(Path.Combine(_root, "pak_mods", "X0000-user-custom.pak")));
    }

    [Fact]
    public void WorldGameIgnoresPakModsMode()
    {
        var worldAppId = _appId + 31;
        var world = new GameProfile(GameId.World, "world-test", "world", "world", "world.exe", worldAppId, "w", "", 0);
        var worldRoot = Path.Combine(_root, "world-game");
        Directory.CreateDirectory(worldRoot);
        File.WriteAllText(Path.Combine(worldRoot, world.ExeName), "fake-game");
        _settings.SetGamePath(world, worldRoot);
        try
        {
            var worldService = new ModService(_settings);
            var staging = CreateStaging("world-mod");
            var parsed = new ParsedMod
            {
                Name = "world-mod",
                SourceFile = Path.Combine(staging, "w.zip"),
                StagingDir = staging,
                Files = [new ParsedFile { SourcePath = WriteFile(staging, "x.pak", "x"), RelativeDest = "x.pak" }]
            };
            var mod = worldService.Install(world, parsed);
            worldService.SetEnabled(world, mod, true);

            // 世界不支持 pak_mods：pak 仍按原名放游戏根目录。
            Assert.True(File.Exists(Path.Combine(worldRoot, "x.pak")));
            Assert.False(Directory.Exists(Path.Combine(worldRoot, "pak_mods")));
        }
        finally
        {
            _settings.Current.GamePaths.Remove(worldAppId);
            if (Directory.Exists(AppPaths.GameDir(worldAppId)))
            {
                Directory.Delete(AppPaths.GameDir(worldAppId), true);
            }
        }
    }

    private ModRecord InstallPak(string name, string content, string displayHint)
    {
        var staging = CreateStaging(name);
        var parsed = new ParsedMod
        {
            Name = name,
            SourceFile = Path.Combine(staging, name + ".zip"),
            StagingDir = staging,
            Files = [new ParsedFile { SourcePath = WriteFile(staging, "old.pak", content), RelativeDest = "old.pak" }]
        };
        var mod = _service.Install(_game, parsed);
        _service.RenameMod(_game, mod, displayHint);
        return mod;
    }

    private string CreateStaging(string name)
    {
        var staging = Path.Combine(_root, "staging", name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        return staging;
    }

    private static string WriteFile(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        _settings.Current.GamePaths.Remove(_appId);
        _settings.Save();
        if (Directory.Exists(AppPaths.GameDir(_appId)))
        {
            Directory.Delete(AppPaths.GameDir(_appId), true);
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
