using MhModManager.Core;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

/// <summary>
/// 游戏根目录(exe 同级)不允许出现 MOD 的杂项文件:封面、说明 txt、多余图片等在解析期丢弃;
/// 旧记录里的此类条目部署时跳过、禁用时清除。豁免:世界 pak 补丁与加载器 dll 必须在游戏根。
/// </summary>
public sealed class ModRootFileTests : IDisposable
{
    private readonly int _appId = Random.Shared.Next(9_000_001, 9_900_000);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mh-mod-manager-service-tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _settings = new();
    private readonly ModService _service;

    public ModRootFileTests()
    {
        Directory.CreateDirectory(_root);
        _settings.Load();
        _settings.Current.CheckGameRunning = false;
        _service = new ModService(_settings);
    }

    [Fact]
    public void WildsImportDropsRootJunkFiles()
    {
        var game = CreateGame(GameId.Wilds, "MonsterHunterWilds.exe", "re_chunk_000.pak.sub_000.pak.patch_", 6);
        var source = NewModDir();
        WriteFile(source, "natives/art/x.tex", "tex");
        WriteFile(source, "说明.txt", "intro");
        WriteFile(source, "cover.png", "cover");
        WriteFile(source, "screenshot.jpg", "second-image");

        var installed = _service.Import(game, source).Mods[0];

        Assert.Equal(["natives/art/x.tex"], installed.Files);
        _service.SetEnabled(game, installed, true);
        Assert.Equal("tex", File.ReadAllText(Path.Combine(_root, "natives", "art", "x.tex")));
        Assert.Empty(NonExeRootFiles());
    }

    [Fact]
    public void WorldImportKeepsRootPakAndLoaderDllOnly()
    {
        var game = CreateGame(GameId.World, "MonsterHunterWorld.exe", "", 0);
        var source = NewModDir();
        WriteFile(source, "nativePC/x.bin", "bin");
        WriteFile(source, "mymod.pak", "pak");
        WriteFile(source, "dinput8.dll", "dll");
        WriteFile(source, "readme.txt", "intro");

        var installed = _service.Import(game, source).Mods[0];

        Assert.Equal(3, installed.Files.Count);
        Assert.Contains("nativePC/x.bin", installed.Files);
        Assert.Contains("mymod.pak", installed.Files);
        Assert.Contains("dinput8.dll", installed.Files);
        Assert.DoesNotContain("readme.txt", installed.Files);

        _service.SetEnabled(game, installed, true);
        Assert.Equal("pak", File.ReadAllText(Path.Combine(_root, "mymod.pak")));
        Assert.Equal("dll", File.ReadAllText(Path.Combine(_root, "dinput8.dll")));
        Assert.Equal("bin", File.ReadAllText(Path.Combine(_root, "nativePC", "x.bin")));
        Assert.False(File.Exists(Path.Combine(_root, "readme.txt")));
        Assert.Equal(2, NonExeRootFiles().Count);
    }

    [Fact]
    public void LegacyRootJunkInRecordIsNotRedeployedAndRemovedOnDisable()
    {
        var game = CreateGame(GameId.World, "MonsterHunterWorld.exe", "", 0);
        var source = NewModDir();
        WriteFile(source, "nativePC/x.bin", "bin");
        var installed = _service.Import(game, source).Mods[0];

        // 模拟旧版本留下的记录与部署:files 根的说明文件已登记,游戏根已有旧副本。
        File.WriteAllText(Path.Combine(AppPaths.ModFilesDir(_appId, installed.Id), "readme.txt"), "mod-copy");
        installed.Files.Add("readme.txt");
        ModRepository.Save(game, installed);
        File.WriteAllText(Path.Combine(_root, "readme.txt"), "old-deployed");

        _service.SetEnabled(game, installed, true);
        Assert.Equal("old-deployed", File.ReadAllText(Path.Combine(_root, "readme.txt")));
        Assert.Equal("bin", File.ReadAllText(Path.Combine(_root, "nativePC", "x.bin")));

        _service.SetEnabled(game, installed, false);
        Assert.False(File.Exists(Path.Combine(_root, "readme.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "nativePC", "x.bin")));
    }

    [Fact]
    public void WorldArchiveWithOnlyRootJunkIsRejected()
    {
        var game = CreateGame(GameId.World, "MonsterHunterWorld.exe", "", 0);
        var source = NewModDir();
        WriteFile(source, "说明.txt", "intro");
        WriteFile(source, "cover.png", "cover");

        Assert.Throws<InvalidOperationException>(() => _service.Import(game, source));
    }

    private GameProfile CreateGame(GameId id, string exeName, string pakPrefix, int pakBaseId)
    {
        var game = new GameProfile(id, "test", "test", "test", exeName, _appId, "test", pakPrefix, pakBaseId);
        File.WriteAllText(Path.Combine(_root, game.ExeName), "fake-game");
        _settings.SetGamePath(game, _root);
        return game;
    }

    private string NewModDir()
    {
        var dir = Path.Combine(_root, "mod-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private List<string> NonExeRootFiles() =>
        Directory.GetFiles(_root, "*", SearchOption.TopDirectoryOnly)
            .Where(file => !file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static void WriteFile(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
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
