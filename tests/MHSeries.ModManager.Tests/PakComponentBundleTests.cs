using MhModManager.Core;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

/// <summary>崛起（pak_mods 模式）下组件化 MOD 的 pak 组件行为。</summary>
public sealed class PakComponentBundleTests : IDisposable
{
    private readonly int _appId = Random.Shared.Next(9_000_001, 9_900_000);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mh-mod-manager-pak-bundle-tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _settings = new();
    private readonly GameProfile _game;
    private readonly ModService _service;

    public PakComponentBundleTests()
    {
        _game = new GameProfile(GameId.Rise, "pak-bundle-test", "rise", "rise", "rise.exe", _appId, "rise",
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
    public void PakComponentsFollowEnableAndOrder()
    {
        var bundle = Path.Combine(_root, "pak-pack");
        Directory.CreateDirectory(bundle);
        WriteFile(bundle, "P/p.pak", "pp");
        WriteFile(bundle, "Q/q.pak", "qq");
        var mod = Assert.Single(_service.Import(_game, bundle).Mods);
        Assert.True(mod.IsBundle);

        var p = mod.Components[0];
        var q = mod.Components[1];

        _service.SetEnabled(_game, mod, true);
        _service.SetComponentEnabled(_game, mod, p.Id, true);
        // 只有一个启用的 pak 时不带 -N 后缀
        Assert.Equal("pp", File.ReadAllText(Path.Combine(_root, "pak_mods", "X0000-pak-pack.pak")));
        Assert.Equal("pak_mods/X0000-pak-pack.pak", mod.DeployPath("c1/p.pak"));

        _service.SetComponentEnabled(_game, mod, q.Id, true);
        Assert.Equal("pp", File.ReadAllText(Path.Combine(_root, "pak_mods", "X0000-pak-pack-1.pak")));
        Assert.Equal("qq", File.ReadAllText(Path.Combine(_root, "pak_mods", "X0000-pak-pack-2.pak")));

        // Q 上移后 -1/-2 编号随新顺序重排
        _service.MoveComponent(_game, mod, q.Id, -1);
        Assert.Equal("qq", File.ReadAllText(Path.Combine(_root, "pak_mods", "X0000-pak-pack-1.pak")));
        Assert.Equal("pp", File.ReadAllText(Path.Combine(_root, "pak_mods", "X0000-pak-pack-2.pak")));

        // 关闭 Q 后只剩一个 pak，回到无后缀命名
        _service.SetComponentEnabled(_game, mod, q.Id, false);
        Assert.Equal("pp", File.ReadAllText(Path.Combine(_root, "pak_mods", "X0000-pak-pack.pak")));
        Assert.False(File.Exists(Path.Combine(_root, "pak_mods", "X0000-pak-pack-1.pak")));
        Assert.False(File.Exists(Path.Combine(_root, "pak_mods", "X0000-pak-pack-2.pak")));
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
        try
        {
            Directory.Delete(_root, true);
            Directory.Delete(AppPaths.GameDir(_appId), true);
        }
        catch
        {
        }
    }
}
