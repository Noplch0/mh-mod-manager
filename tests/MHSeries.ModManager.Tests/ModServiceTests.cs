using HuntForge.Core;
using HuntForge.Models;
using Xunit;

namespace HuntForge.Tests;

public sealed class ModServiceTests : IDisposable
{
    private readonly int _appId = Random.Shared.Next(9_000_001, 9_900_000);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "huntforge-service-tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _settings = new();
    private readonly GameProfile _game;
    private readonly ModService _service;

    public ModServiceTests()
    {
        _game = new GameProfile(GameId.World, "test", "test", "test", "test.exe", _appId, "test", "", 0);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, _game.ExeName), "fake-game");
        _settings.Load();
        _settings.Current.InstallOption = 0;
        _settings.Current.CheckGameRunning = false;
        _settings.SetGamePath(_game, _root);
        _service = new ModService(_settings);
    }

    [Fact]
    public void OverlappingModsRestoreTheOriginalGameFile()
    {
        var destination = WriteFile(_root, "nativePC/shared.bin", "original");
        var first = _service.Install(_game, CreateParsed("first", "first-mod"));
        var second = _service.Install(_game, CreateParsed("second", "second-mod"));

        _service.SetEnabled(_game, first, true);
        Assert.Equal("first-mod", File.ReadAllText(destination));
        _service.SetEnabled(_game, second, true);
        Assert.Equal("second-mod", File.ReadAllText(destination));
        _service.SetEnabled(_game, second, false);
        Assert.Equal("first-mod", File.ReadAllText(destination));
        _service.SetEnabled(_game, first, false);

        Assert.Equal("original", File.ReadAllText(destination));
    }

    [Fact]
    public void InstallCopiesEveryFileIncludingExtensionlessFiles()
    {
        var staging = CreateStaging("complete");
        var binary = WriteFile(staging, "one.bin", "binary");
        var extensionless = WriteFile(staging, "configuration", "config");
        var parsed = new ParsedMod
        {
            Name = "complete",
            SourceFile = Path.Combine(staging, "package.zip"),
            StagingDir = staging,
            Files =
            [
                new ParsedFile { SourcePath = binary, RelativeDest = "nativePC/one.bin" },
                new ParsedFile { SourcePath = extensionless, RelativeDest = "nativePC/configuration" }
            ]
        };

        var installed = _service.Install(_game, parsed);
        var filesDir = AppPaths.ModFilesDir(_appId, installed.Id);

        Assert.Equal(2, installed.Files.Count);
        Assert.Equal("binary", File.ReadAllText(Path.Combine(filesDir, "nativePC", "one.bin")));
        Assert.Equal("config", File.ReadAllText(Path.Combine(filesDir, "nativePC", "configuration")));
    }

    [Fact]
    public void NewModsGoIntoTheDefaultGroupAtTheBottom()
    {
        var first = _service.Install(_game, CreateParsed("first", "first-mod"));
        var custom = _service.CreateGroup(_game, "外观");
        var second = _service.Install(_game, CreateParsed("second", "second-mod"));
        var groups = _service.GetGroups(_game);

        Assert.Equal(groups.First(group => group.IsDefault).Id, first.GroupId);
        Assert.Equal(groups.First(group => group.IsDefault).Id, second.GroupId);
        Assert.True(custom.Index < groups.First(group => group.IsDefault).Index);
    }

    [Fact]
    public void LaterGroupOverridesEarlierGroupAndRestoresWhenDisabled()
    {
        var destination = WriteFile(_root, "nativePC/shared.bin", "original");
        var earlier = _service.Install(_game, CreateParsed("earlier", "group-one"));
        var later = _service.Install(_game, CreateParsed("later", "group-two"));
        var firstGroup = _service.CreateGroup(_game, "一组");
        var secondGroup = _service.CreateGroup(_game, "二组");
        _service.MoveModToGroup(_game, earlier, firstGroup.Id);
        _service.MoveModToGroup(_game, later, secondGroup.Id);

        _service.SetEnabled(_game, earlier, true);
        _service.SetEnabled(_game, later, true);
        Assert.Equal("group-two", File.ReadAllText(destination));

        _service.SetEnabled(_game, later, false);
        Assert.Equal("group-one", File.ReadAllText(destination));
    }

    [Fact]
    public void GroupToggleEnablesAndDisablesEveryMember()
    {
        var first = _service.Install(_game, CreateParsed("one", "one"));
        var second = _service.Install(_game, CreateParsed("two", "two"));
        var group = _service.CreateGroup(_game, "批量");
        _service.MoveModToGroup(_game, first, group.Id);
        _service.MoveModToGroup(_game, second, group.Id);

        _service.SetGroupEnabled(_game, group, true);
        Assert.True(first.Enabled);
        Assert.True(second.Enabled);

        _service.SetGroupEnabled(_game, group, false);
        Assert.False(first.Enabled);
        Assert.False(second.Enabled);
    }

    [Fact]
    public void DeletingAGroupMovesModsBackToDefault()
    {
        var mod = _service.Install(_game, CreateParsed("orphan", "data"));
        var group = _service.CreateGroup(_game, "临时");
        _service.MoveModToGroup(_game, mod, group.Id);
        _service.DeleteGroup(_game, group);

        var fallback = _service.GetGroups(_game).First(item => item.IsDefault);
        Assert.Equal(fallback.Id, mod.GroupId);
        Assert.DoesNotContain(_service.GetGroups(_game), item => item.Id == group.Id);
    }

    private ParsedMod CreateParsed(string name, string content)
    {
        var staging = CreateStaging(name);
        var source = WriteFile(staging, "shared.bin", content);
        return new ParsedMod
        {
            Name = name,
            SourceFile = Path.Combine(staging, name + ".zip"),
            StagingDir = staging,
            Files = [new ParsedFile { SourcePath = source, RelativeDest = "nativePC/shared.bin" }]
        };
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
