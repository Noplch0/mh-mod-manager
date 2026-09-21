using MhModManager.Core;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

/// <summary>组件化 MOD：导入为单记录、组件开关/排序、优先级覆盖、更新与迁移。</summary>
public sealed class BundleComponentTests : IDisposable
{
    private readonly int _appId = Random.Shared.Next(9_000_001, 9_900_000);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mh-mod-manager-bundle-tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _settings = new();
    private readonly GameProfile _game;
    private readonly ModService _service;

    public BundleComponentTests()
    {
        _game = new GameProfile(GameId.World, "bundle-test", "test", "test", "test.exe", _appId, "test", "", 0);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, _game.ExeName), "fake-game");
        _settings.Load();
        _settings.Current.InstallOption = 0;
        _settings.Current.CheckGameRunning = false;
        _settings.SetGamePath(_game, _root);
        _service = new ModService(_settings);
    }

    [Fact]
    public void ComponentToggleDeploysInOrderAndRestoresOnDisable()
    {
        var mod = ImportTwoComponentPack();
        var alpha = mod.Components[0];
        var beta = mod.Components[1];
        var deployed = Path.Combine(_root, "nativePC", "shared.bin");

        // 总开关关闭时切组件只记录状态
        _service.SetComponentEnabled(_game, mod, alpha.Id, true);
        Assert.True(alpha.Enabled);
        Assert.False(File.Exists(deployed));

        _service.SetEnabled(_game, mod, true);
        Assert.Equal("alpha", File.ReadAllText(deployed));

        // 同开时靠后的 Beta 覆盖
        _service.SetComponentEnabled(_game, mod, beta.Id, true);
        Assert.Equal("beta", File.ReadAllText(deployed));

        // 关 Beta 恢复 Alpha
        _service.SetComponentEnabled(_game, mod, beta.Id, false);
        Assert.Equal("alpha", File.ReadAllText(deployed));

        // 全关无残留
        _service.SetEnabled(_game, mod, false);
        Assert.False(File.Exists(deployed));
    }

    [Fact]
    public void ComponentReorderChangesWinnerImmediately()
    {
        var mod = ImportTwoComponentPack();
        var alpha = mod.Components[0];
        var beta = mod.Components[1];
        var deployed = Path.Combine(_root, "nativePC", "shared.bin");

        _service.SetComponentEnabled(_game, mod, alpha.Id, true);
        _service.SetComponentEnabled(_game, mod, beta.Id, true);
        _service.SetEnabled(_game, mod, true);
        Assert.Equal("beta", File.ReadAllText(deployed));

        // Beta 上移后 Alpha 变成靠后者，覆盖关系立即反转
        _service.MoveComponent(_game, mod, beta.Id, -1);
        Assert.Equal(["Beta", "Alpha"], mod.Components.Select(item => item.Name));
        Assert.Equal("alpha", File.ReadAllText(deployed));

        // 总开关关闭时调序只改列表
        _service.SetEnabled(_game, mod, false);
        _service.MoveComponent(_game, mod, beta.Id, 1);
        Assert.Equal(["Alpha", "Beta"], mod.Components.Select(item => item.Name));
        Assert.False(File.Exists(deployed));
    }

    [Fact]
    public void MasterEnableWithNoComponentsDeploysNothing()
    {
        var mod = ImportTwoComponentPack();
        var deployed = Path.Combine(_root, "nativePC", "shared.bin");

        _service.SetEnabled(_game, mod, true);
        Assert.True(mod.Enabled);
        Assert.False(File.Exists(deployed));

        _service.SetComponentEnabled(_game, mod, mod.Components[0].Id, true);
        Assert.Equal("alpha", File.ReadAllText(deployed));
    }

    [Fact]
    public void FoldersAndLoosePaksBecomeComponents()
    {
        var bundle = Path.Combine(_root, "ex2-shape");
        Directory.CreateDirectory(bundle);
        WriteFile(bundle, "ModA/nativePC/a.bin", "a");
        WriteFile(bundle, "ModB/nativePC/b.bin", "b");
        WriteFile(bundle, "ModC/nativePC/c.bin", "c");
        WriteFile(bundle, "loose one.pak", "p1");
        WriteFile(bundle, "loose_two.pak", "p2");
        WriteFile(bundle, "READ_THIS.txt", "readme");

        var mod = Assert.Single(_service.Import(_game, bundle).Mods);

        Assert.True(mod.IsBundle);
        Assert.Equal(5, mod.Components.Count);
        Assert.Equal(["loose one", "loose_two", "ModA", "ModB", "ModC"], mod.Components.Select(item => item.Name));
        Assert.Contains("c1/loose one.pak", mod.Components[0].Files);

        // 世界：散装 pak 组件部署到游戏根目录
        _service.SetEnabled(_game, mod, true);
        _service.SetComponentEnabled(_game, mod, mod.Components[0].Id, true);
        Assert.Equal("p1", File.ReadAllText(Path.Combine(_root, "loose one.pak")));
        _service.SetComponentEnabled(_game, mod, mod.Components[2].Id, true);
        Assert.Equal("a", File.ReadAllText(Path.Combine(_root, "nativePC", "a.bin")));
    }

    [Fact]
    public void SingleLoosePakStaysNormalMod()
    {
        var bundle = Path.Combine(_root, "only-pak");
        Directory.CreateDirectory(bundle);
        WriteFile(bundle, "only.pak", "pak");

        var mod = Assert.Single(_service.Import(_game, bundle).Mods);

        Assert.False(mod.IsBundle);
        Assert.Empty(mod.Components);
    }

    [Fact]
    public void UpdateBundlePreservesEnabledComponentsByName()
    {
        var pack1 = Path.Combine(_root, "pack-one");
        Directory.CreateDirectory(pack1);
        WriteFile(pack1, "Alpha/nativePC/alpha.bin", "alpha1");
        WriteFile(pack1, "Beta/nativePC/beta.bin", "beta1");
        var mod = Assert.Single(_service.Import(_game, pack1).Mods);
        _service.SetComponentEnabled(_game, mod, mod.Components[1].Id, true);
        _service.SetEnabled(_game, mod, true);

        var pack2 = Path.Combine(_root, "pack-two");
        Directory.CreateDirectory(pack2);
        WriteFile(pack2, "Beta/nativePC/beta.bin", "beta2");
        WriteFile(pack2, "Gamma/nativePC/gamma.bin", "gamma");
        _service.Update(_game, mod, pack2);

        Assert.True(mod.IsBundle);
        Assert.Equal(["Beta", "Gamma"], mod.Components.Select(item => item.Name));
        Assert.True(mod.Components[0].Enabled);
        Assert.False(mod.Components[1].Enabled);
        Assert.True(mod.Enabled);
        Assert.Equal("beta2", File.ReadAllText(Path.Combine(_root, "nativePC", "beta.bin")));
        Assert.False(File.Exists(Path.Combine(_root, "nativePC", "gamma.bin")));
    }

    [Fact]
    public void ComponentCoverIsCopiedForPreview()
    {
        var bundle = Path.Combine(_root, "cover-pack");
        Directory.CreateDirectory(bundle);
        WriteFile(bundle, "Alpha/preview.png", new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        WriteFile(bundle, "Alpha/nativePC/a.bin", "a");
        WriteFile(bundle, "Beta/nativePC/b.bin", "b");

        var mod = Assert.Single(_service.Import(_game, bundle).Mods);

        var cover = Path.Combine(AppPaths.ModDir(_appId, mod.Id), "covers", "c1.png");
        Assert.True(File.Exists(cover));
        Assert.Equal(cover, mod.PreviewImage);
        Assert.Equal(cover, _service.ComponentPreviewPath(_game, mod, 1));
        Assert.Equal("", _service.ComponentPreviewPath(_game, mod, 2));
    }

    [Fact]
    public void MoveBundleToAnotherGroupKeepsSingleRecord()
    {
        var mod = ImportTwoComponentPack();
        var group = _service.CreateGroup(_game, "我的收藏");

        _service.MoveModToGroup(_game, mod, group.Id);

        Assert.Single(_service.GetMods(_game));
        Assert.Equal(group.Id, mod.GroupId);
        Assert.True(mod.IsBundle);
    }

    [Fact]
    public void LegacySameSourceGroupMigratesToBundleMod()
    {
        var first = _service.Install(_game, ParsedWithSource("pack.zip", "first", "one"));
        var second = _service.Install(_game, ParsedWithSource("pack.zip", "second", "two"));
        var manual = _service.Install(_game, ParsedWithSource("other.zip", "manual", "m"));
        var combo = _service.CreateGroup(_game, "Combo");
        var custom = _service.CreateGroup(_game, "Custom");
        _service.MoveModToGroup(_game, first, combo.Id);
        _service.MoveModToGroup(_game, second, combo.Id);
        _service.MoveModToGroup(_game, manual, custom.Id);
        _service.SetEnabled(_game, first, true);

        // 新的服务实例触发重新加载与迁移
        var fresh = new ModService(_settings);
        var mods = fresh.GetMods(_game);

        var bundleMod = Assert.Single(mods.Where(item => item.IsBundle));
        Assert.Equal(first.Id, bundleMod.Id);
        Assert.Equal(["first", "second"], bundleMod.Components.Select(item => item.Name));
        Assert.True(bundleMod.Components[0].Enabled);
        Assert.False(bundleMod.Components[1].Enabled);
        Assert.True(bundleMod.Enabled);
        Assert.DoesNotContain(fresh.GetGroups(_game), group => group.Name == "Combo");
        Assert.Contains(fresh.GetGroups(_game), group => group.Name == "Custom");

        var keeperFiles = AppPaths.ModFilesDir(_appId, first.Id);
        Assert.Equal("one", File.ReadAllText(Path.Combine(keeperFiles, "c1", "nativePC", "shared.bin")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(keeperFiles, "c2", "nativePC", "shared.bin")));

        // 手动分组里的 mod 不受影响，已部署文件保持原状
        var kept = Assert.Single(mods.Where(item => !item.IsBundle));
        Assert.Equal(custom.Id, kept.GroupId);
        Assert.Equal("one", File.ReadAllText(Path.Combine(_root, "nativePC", "shared.bin")));
    }

    private ModRecord ImportTwoComponentPack()
    {
        var bundle = Path.Combine(_root, "life-pack");
        Directory.CreateDirectory(bundle);
        WriteFile(bundle, "Alpha/nativePC/shared.bin", "alpha");
        WriteFile(bundle, "Beta/nativePC/shared.bin", "beta");
        return Assert.Single(_service.Import(_game, bundle).Mods);
    }

    private ParsedMod ParsedWithSource(string sourceName, string name, string content)
    {
        var staging = Path.Combine(_root, "staging", name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var source = WriteFile(staging, "nativePC/shared.bin", content);
        return new ParsedMod
        {
            Name = name,
            SourceFile = Path.Combine(staging, sourceName),
            StagingDir = staging,
            Files = [new ParsedFile { SourcePath = source, RelativeDest = "nativePC/shared.bin" }]
        };
    }

    private static string WriteFile(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string WriteFile(string root, string relative, byte[] content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
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
