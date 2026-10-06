using MhModManager.Core;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

/// <summary>崛起/荒野 pak 组件的跨 MOD 优先级:X 编号必须跟随列表顺序,靠后(下方)MOD 的 pak 编号更大。</summary>
public sealed class CrossModPakComponentTests : IDisposable
{
    private readonly int _appId = Random.Shared.Next(9_000_001, 9_900_000);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mh-mod-manager-crosspak-tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _settings = new();
    private readonly GameProfile _game;
    private readonly ModService _service;

    public CrossModPakComponentTests()
    {
        _game = new GameProfile(GameId.Wilds, "crosspak", "test", "test", "test.exe", _appId, "test",
            "re_chunk_000.pak.sub_000.pak.patch_", 6);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, _game.ExeName), "fake-game");
        _settings.Load();
        _settings.Current.InstallOption = 0;
        _settings.Current.CheckGameRunning = false;
        _settings.SetGamePath(_game, _root);
        _service = new ModService(_settings);
    }

    [Fact]
    public void EnablingUpperComponentAfterLowerKeepsLowerPakNumberHigher()
    {
        var upper = ImportPakBundle("UpperPack", "upper");
        var lower = ImportPakBundle("LowerPack", "lower");

        // 先启用下方 MOD 的组件
        _service.SetEnabled(_game, lower, true);
        _service.SetComponentEnabled(_game, lower, PakComponent(lower).Id, true);
        Assert.Single(PakMods());

        // 再启用上方 MOD 的组件:靠前的编号必须更小
        _service.SetEnabled(_game, upper, true);
        _service.SetComponentEnabled(_game, upper, PakComponent(upper).Id, true);

        var paks = PakMods();
        Assert.Equal(2, paks.Count);
        var upperNumber = PakNumber(paks, "UpperPack");
        var lowerNumber = PakNumber(paks, "LowerPack");
        Assert.True(upperNumber < lowerNumber,
            $"上方编号 {upperNumber} 应小于下方编号 {lowerNumber}: {string.Join(", ", paks.Select(Path.GetFileName))}");
    }

    [Fact]
    public void ReorderingModsThenTogglingComponentsKeepsNumberingInVisualOrder()
    {
        // 导入顺序:One 在上、Two 在下;随后把 Two 上移到 One 之上(调序)。
        var one = ImportPakBundle("OnePack", "one");
        var two = ImportPakBundle("TwoPack", "two");
        _service.Move(_game, two, -1);
        // 现在视觉顺序:Two(上)、One(下)。

        // 先启用下方(One)的组件,再启用上方(Two)的组件
        _service.SetEnabled(_game, one, true);
        _service.SetComponentEnabled(_game, one, PakComponent(one).Id, true);
        _service.SetEnabled(_game, two, true);
        _service.SetComponentEnabled(_game, two, PakComponent(two).Id, true);

        var paks = PakMods();
        var twoNumber = PakNumber(paks, "TwoPack");
        var oneNumber = PakNumber(paks, "OnePack");
        Assert.True(twoNumber < oneNumber,
            $"上方(Two)编号 {twoNumber} 应小于下方(One)编号 {oneNumber}: {string.Join(", ", paks.Select(Path.GetFileName))}");
    }

    [Fact]
    public void MovingModsAfterComponentsEnabledRenumbersPaks()
    {
        var one = ImportPakBundle("OnePack", "one");
        var two = ImportPakBundle("TwoPack", "two");

        _service.SetEnabled(_game, one, true);
        _service.SetComponentEnabled(_game, one, PakComponent(one).Id, true);
        _service.SetEnabled(_game, two, true);
        _service.SetComponentEnabled(_game, two, PakComponent(two).Id, true);
        var before = PakNumber(PakMods(), "OnePack");
        Assert.True(before < PakNumber(PakMods(), "TwoPack"));

        // One 下移到 Two 之下:编号立即反转
        _service.Move(_game, one, 1);

        var paks = PakMods();
        Assert.True(PakNumber(paks, "TwoPack") < PakNumber(paks, "OnePack"),
            $"调序后编号未跟随列表顺序: {string.Join(", ", paks.Select(Path.GetFileName))}");
    }

    private List<string> PakMods()
    {
        var dir = Path.Combine(_root, PakModsManager.DirName);
        return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.pak").ToList() : [];
    }

    private static int PakNumber(List<string> paks, string modName)
    {
        var file = paks.FirstOrDefault(item =>
            Path.GetFileName(item).Contains(modName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"找不到 {modName} 的 pak: {string.Join(", ", paks.Select(Path.GetFileName))}");
        var name = Path.GetFileName(file);
        return int.Parse(name[1..name.IndexOf('-')]);
    }

    private ModComponent PakComponent(ModRecord mod) =>
        mod.Components.First(item => item.Files.Any(file => file.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)));

    private ModRecord ImportPakBundle(string packName, string pakContent)
    {
        var pack = Path.Combine(_root, packName);
        Directory.CreateDirectory(pack);
        WriteFile(pack, $"PartA/{packName.ToLowerInvariant()}-armor.pak", pakContent);
        WriteFile(pack, "PartB/natives/other.bin", packName);
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
