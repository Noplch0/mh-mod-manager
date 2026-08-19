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
    public void MoveModsToGroupMovesEverySelectedMod()
    {
        var first = _service.Install(_game, CreateParsed("one", "one"));
        var second = _service.Install(_game, CreateParsed("two", "two"));
        var leftover = _service.Install(_game, CreateParsed("three", "three"));
        var group = _service.CreateGroup(_game, "外观");

        _service.MoveModsToGroup(_game, [first, second], group.Id);

        Assert.Equal(group.Id, first.GroupId);
        Assert.Equal(group.Id, second.GroupId);
        Assert.NotEqual(group.Id, leftover.GroupId);
        Assert.Equal(1, first.Index);
        Assert.Equal(2, second.Index);
    }

    [Fact]
    public void GroupCollapseIsPersisted()
    {
        var group = _service.CreateGroup(_game, "折叠");
        _service.SetGroupCollapsed(_game, group, true);

        Assert.True(group.Collapsed);
        var reloaded = JsonUtil.Load(AppPaths.GroupsFile(_appId), new List<ModGroup>());
        Assert.Contains(reloaded, item => item.Id == group.Id && item.Collapsed);
    }

    [Fact]
    public void InstallKeepsPreviewImageExtension()
    {
        var staging = CreateStaging("preview-mod");
        var source = WriteFile(staging, "nativePC/shared.bin", "preview-mod");
        var preview = Path.Combine(staging, "preview.jpg");
        File.WriteAllBytes(preview, [0xFF, 0xD8, 0xFF, 0xD9]);
        var parsed = new ParsedMod
        {
            Name = "preview-mod",
            SourceFile = Path.Combine(staging, "preview-mod.zip"),
            StagingDir = staging,
            PreviewSource = preview,
            Files = [new ParsedFile { SourcePath = source, RelativeDest = "nativePC/shared.bin" }]
        };

        var installed = _service.Install(_game, parsed);
        var previewPath = _service.PreviewPath(_game, installed);

        Assert.True(File.Exists(previewPath));
        Assert.Equal(".jpg", Path.GetExtension(previewPath), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(installed.Files, file => file.Contains("screenshot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RenameModUpdatesDisplayName()
    {
        var mod = _service.Install(_game, CreateParsed("old-name", "data"));
        _service.RenameMod(_game, mod, "自定义名称");

        Assert.Equal("自定义名称", mod.DisplayName);
        var reloaded = JsonUtil.Load(Path.Combine(AppPaths.ModDir(_appId, mod.Id), "info.json"), new ModRecord());
        Assert.Equal("自定义名称", reloaded.DisplayName);
    }

    [Fact]
    public void BundleWithIndependentFoldersCreatesAGroup()
    {
        var bundle = Path.Combine(_root, "Item Duration Mod - All-in-One.rar");
        Directory.CreateDirectory(bundle);
        WriteFile(bundle, "Duration 30/nativePC/one.bin", "one");
        WriteFile(bundle, "Duration 60/nativePC/two.bin", "two");
        WriteFile(bundle, "Duration 90/nativePC/three.bin", "three");

        var result = _service.Import(_game, bundle);

        Assert.NotNull(result.Group);
        Assert.Equal("Item Duration Mod - All-in-One.rar", result.Group!.Name);
        Assert.Equal(3, result.Mods.Count);
        Assert.All(result.Mods, mod => Assert.Equal(result.Group.Id, mod.GroupId));
        Assert.Contains(result.Mods, mod => mod.DisplayName == "Duration 30");
        Assert.Contains(result.Mods, mod => mod.DisplayName == "Duration 60");
        Assert.Contains(result.Mods, mod => mod.DisplayName == "Duration 90");
    }

    [Fact]
    public void SingleModArchiveDoesNotCreateAGroup()
    {
        var archive = Path.Combine(_root, "single-mod");
        Directory.CreateDirectory(archive);
        WriteFile(archive, "nativePC/shared.bin", "one");

        var result = _service.Import(_game, archive);

        Assert.Null(result.Group);
        Assert.Single(result.Mods);
        Assert.Equal(_service.GetGroups(_game).First(group => group.IsDefault).Id, result.Mods[0].GroupId);
    }

    [Fact]
    public void UpdateSingleArchiveReplacesFilesAndKeepsIdentity()
    {
        var original = _service.Install(_game, CreateParsed("old", "old-data"));
        _service.RenameMod(_game, original, "自定义名称");
        var replacement = Path.Combine(_root, "updated-mod");
        Directory.CreateDirectory(replacement);
        WriteFile(replacement, "nativePC/shared.bin", "new-data");
        WriteFile(replacement, "nativePC/extra.bin", "extra");

        var result = _service.Update(_game, original, replacement);

        Assert.Null(result.Group);
        Assert.Equal(original.Id, result.Mods[0].Id);
        Assert.Equal("自定义名称", original.DisplayName);
        Assert.Equal(original.GroupId, result.Mods[0].GroupId);
        var filesDir = AppPaths.ModFilesDir(_appId, original.Id);
        Assert.Equal("new-data", File.ReadAllText(Path.Combine(filesDir, "nativePC", "shared.bin")));
        Assert.True(File.Exists(Path.Combine(filesDir, "nativePC", "extra.bin")));
    }

    [Fact]
    public void UpdateBundleRemovesOriginalAndCreatesGroup()
    {
        var original = _service.Install(_game, CreateParsed("old", "old-data"));
        var bundle = Path.Combine(_root, "Combo Pack");
        Directory.CreateDirectory(bundle);
        WriteFile(bundle, "Alpha/nativePC/a.bin", "a");
        WriteFile(bundle, "Beta/nativePC/b.bin", "b");

        var result = _service.Update(_game, original, bundle);

        Assert.NotNull(result.Group);
        Assert.Equal("Combo Pack", result.Group!.Name);
        Assert.Equal(2, result.Mods.Count);
        Assert.DoesNotContain(_service.GetMods(_game), item => item.DisplayName == "old");
        Assert.Contains(result.Mods, mod => mod.DisplayName == "Alpha");
        Assert.Contains(result.Mods, mod => mod.DisplayName == "Beta");
        Assert.All(result.Mods, mod => Assert.Equal(result.Group.Id, mod.GroupId));
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
