using MhModManager.Core;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

/// <summary>跨 MOD 的组件开关:同文件冲突时列表靠后(下方)的 MOD 必须覆盖靠前(上方),与启用先后无关。</summary>
public sealed class CrossModComponentTests : IDisposable
{
    private readonly int _appId = Random.Shared.Next(9_000_001, 9_900_000);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mh-mod-manager-cross-tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _settings = new();
    private readonly GameProfile _game;
    private readonly ModService _service;

    public CrossModComponentTests()
    {
        _game = new GameProfile(GameId.World, "cross-test", "test", "test", "test.exe", _appId, "test", "", 0);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, _game.ExeName), "fake-game");
        _settings.Load();
        _settings.Current.InstallOption = 0;
        _settings.Current.CheckGameRunning = false;
        _settings.SetGamePath(_game, _root);
        _service = new ModService(_settings);
    }

    [Fact]
    public void EnablingUpperComponentAfterLowerKeepsLowerFile()
    {
        var upper = ImportBundle("upper-pack", "upper-content");
        var lower = ImportBundle("lower-pack", "lower-content");
        var deployed = Path.Combine(_root, "nativePC", "shared.bin");

        // 先启用下方 MOD 的组件
        _service.SetEnabled(_game, lower, true);
        _service.SetComponentEnabled(_game, lower, SharedComponent(lower).Id, true);
        Assert.Equal("lower-content", File.ReadAllText(deployed));

        // 再启用上方 MOD 的组件:靠前的不得覆盖靠后的
        _service.SetEnabled(_game, upper, true);
        _service.SetComponentEnabled(_game, upper, SharedComponent(upper).Id, true);

        Assert.Equal("lower-content", File.ReadAllText(deployed));
    }

    [Fact]
    public void ComponentsToggledWhileMasterOffThenMastersEnabledKeepLowerFile()
    {
        var upper = ImportBundle("upper-pack", "upper-content");
        var lower = ImportBundle("lower-pack", "lower-content");
        var deployed = Path.Combine(_root, "nativePC", "shared.bin");

        // 总开关全关时先把两个组件都拨到开
        _service.SetComponentEnabled(_game, lower, SharedComponent(lower).Id, true);
        _service.SetComponentEnabled(_game, upper, SharedComponent(upper).Id, true);
        Assert.False(File.Exists(deployed));

        _service.SetEnabled(_game, lower, true);
        Assert.Equal("lower-content", File.ReadAllText(deployed));
        _service.SetEnabled(_game, upper, true);

        Assert.Equal("lower-content", File.ReadAllText(deployed));
    }

    [Fact]
    public void DifferentGroupsKeepGroupOrderWhenTogglingComponents()
    {
        var upper = ImportBundle("upper-pack", "upper-content");
        var lower = ImportBundle("lower-pack", "lower-content");
        var topGroup = _service.CreateGroup(_game, "一组");
        _service.MoveModToGroup(_game, upper, topGroup.Id);
        var deployed = Path.Combine(_root, "nativePC", "shared.bin");

        _service.SetEnabled(_game, lower, true);
        _service.SetComponentEnabled(_game, lower, SharedComponent(lower).Id, true);
        _service.SetEnabled(_game, upper, true);
        _service.SetComponentEnabled(_game, upper, SharedComponent(upper).Id, true);

        Assert.Equal("lower-content", File.ReadAllText(deployed));
    }

    [Fact]
    public void PlainUpperModComponentlessStillLosesToLowerBundle()
    {
        var plainDir = Path.Combine(_root, "plain-pack");
        Directory.CreateDirectory(plainDir);
        WriteFile(plainDir, "nativePC/shared.bin", "upper-content");
        WriteFile(plainDir, "nativePC/plain-only.bin", "plain");
        var upper = _service.Import(_game, plainDir).Mods[0];

        var lower = ImportBundle("lower-pack", "lower-content");
        var deployed = Path.Combine(_root, "nativePC", "shared.bin");

        _service.SetEnabled(_game, lower, true);
        _service.SetComponentEnabled(_game, lower, SharedComponent(lower).Id, true);
        _service.SetEnabled(_game, upper, true);

        Assert.Equal("lower-content", File.ReadAllText(deployed));
    }

    [Fact]
    public void EnablingLowerComponentAfterUpperOverridesUpperFile()
    {
        var upper = ImportBundle("upper-pack", "upper-content");
        var lower = ImportBundle("lower-pack", "lower-content");
        var deployed = Path.Combine(_root, "nativePC", "shared.bin");

        _service.SetEnabled(_game, upper, true);
        _service.SetComponentEnabled(_game, upper, SharedComponent(upper).Id, true);
        Assert.Equal("upper-content", File.ReadAllText(deployed));

        _service.SetEnabled(_game, lower, true);
        _service.SetComponentEnabled(_game, lower, SharedComponent(lower).Id, true);

        Assert.Equal("lower-content", File.ReadAllText(deployed));
    }

    [Fact]
    public void DisablingUpperComponentRestoresLowerFile()
    {
        var upper = ImportBundle("upper-pack", "upper-content");
        var lower = ImportBundle("lower-pack", "lower-content");
        var deployed = Path.Combine(_root, "nativePC", "shared.bin");

        _service.SetEnabled(_game, lower, true);
        _service.SetComponentEnabled(_game, lower, SharedComponent(lower).Id, true);
        _service.SetEnabled(_game, upper, true);
        _service.SetComponentEnabled(_game, upper, SharedComponent(upper).Id, true);
        _service.SetComponentEnabled(_game, upper, SharedComponent(upper).Id, false);

        Assert.Equal("lower-content", File.ReadAllText(deployed));
    }

    private ModComponent SharedComponent(ModRecord mod) =>
        mod.Components.First(item => item.Files.Any(file => file.EndsWith("shared.bin", StringComparison.OrdinalIgnoreCase)));

    private ModRecord ImportBundle(string packName, string sharedContent)
    {
        var pack = Path.Combine(_root, packName);
        Directory.CreateDirectory(pack);
        WriteFile(pack, "PartA/nativePC/shared.bin", sharedContent);
        WriteFile(pack, "PartB/nativePC/other.bin", packName);
        return _service.Import(_game, pack).Mods[0];
    }

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
