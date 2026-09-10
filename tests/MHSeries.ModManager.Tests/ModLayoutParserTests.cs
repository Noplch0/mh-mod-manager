using MhModManager.Core;
using MhModManager.Models;
using System.IO.Compression;
using Xunit;

namespace MhModManager.Tests;

public sealed class ModLayoutParserTests : IDisposable
{
    private readonly List<string> _tempRoots = [];

    [Fact]
    public void GroupOrderPutsWholeEarlierGroupsBeforeLaterGroups()
    {
        var groups = new List<ModGroup>
        {
            new() { Id = 1, Index = 1 },
            new() { Id = 2, Index = 2 }
        };
        var first = new ModRecord { Id = 10, GroupId = 1, Index = 10 };
        var second = new ModRecord { Id = 11, GroupId = 2, Index = 1 };

        Assert.True(GroupOrder.Compare(first, second, groups) < 0);
        Assert.Equal(first, GroupOrder.Ordered([second, first], groups).First());
    }

    [Fact]
    public void ProfilesContainTheThreeMonsterHunterGames()
    {
        Assert.Equal(3, GameProfile.All.Count);
        Assert.Equal("nativePC", ModLayoutParser.DetectPrefix(GameProfile.Get(GameId.World), CreateRoot("pl")));
        Assert.Equal("natives", ModLayoutParser.DetectPrefix(GameProfile.Get(GameId.Rise), CreateRoot("weapon")));
        Assert.Equal("natives", ModLayoutParser.DetectPrefix(GameProfile.Get(GameId.Wilds), CreateRoot("art")));
    }

    [Fact]
    public void ExistingDeployRootsAreNotDuplicated()
    {
        var world = CreateRoot("nativePC");
        var rise = CreateRoot("natives");
        var wilds = CreateRoot("reframework");

        Assert.Equal("", ModLayoutParser.DetectPrefix(GameProfile.Get(GameId.World), world));
        Assert.Equal("", ModLayoutParser.DetectPrefix(GameProfile.Get(GameId.Rise), rise));
        Assert.Equal("", ModLayoutParser.DetectPrefix(GameProfile.Get(GameId.Wilds), wilds));
    }

    [Theory]
    [InlineData("re_chunk_000.pak.patch_001.pak", 1)]
    [InlineData("re_chunk_000.pak.sub_000.pak.patch_006.pak", 6)]
    [InlineData("not-a-patch.pak", 0)]
    public void PakNumbersFollowTheOriginalNamingRules(string name, int expected)
    {
        Assert.Equal(expected, ModLayoutParser.ParsePakNumber(name));
    }

    [Fact]
    public void NexusNamesAreExtractedFromCommonFileNames()
    {
        var result = NexusNames.ParseNexusStem("cool-weapon-123-v2-1");

        Assert.NotNull(result);
        Assert.Equal(123, result.Value.ModId);
        Assert.Equal("cool weapon", result.Value.Name);
        Assert.Equal("2.1", result.Value.Version);
    }

    [Fact]
    public void WorldModelFolderKeepsThePlDirectory()
    {
        var root = CreateRoot("pl");
        var file = WriteFile(root, "pl/equip/test.bin", "world-mod");
        var parsed = ModLayoutParser.Parse(GameProfile.Get(GameId.World), WriteFile(root, "source.tmp", ""), root);

        var item = Assert.Single(parsed.Files);
        Assert.Equal(file, item.SourcePath);
        Assert.Equal("nativePC/pl/equip/test.bin", item.RelativeDest);
    }

    [Fact]
    public void RiseAndWildsModelFoldersKeepTheirNativeRoots()
    {
        var riseRoot = CreateRoot("weapon");
        WriteFile(riseRoot, "weapon/sword/test.mesh", "rise-mod");
        var rise = ModLayoutParser.Parse(GameProfile.Get(GameId.Rise), WriteFile(riseRoot, "source.tmp", ""), riseRoot);

        var wildsRoot = CreateRoot("art");
        WriteFile(wildsRoot, "art/model/test.mesh", "wilds-mod");
        var wilds = ModLayoutParser.Parse(GameProfile.Get(GameId.Wilds), WriteFile(wildsRoot, "source.tmp", ""), wildsRoot);

        Assert.Equal("natives/weapon/sword/test.mesh", Assert.Single(rise.Files).RelativeDest);
        Assert.Equal("natives/art/model/test.mesh", Assert.Single(wilds.Files).RelativeDest);
    }

    [Fact]
    public void ExplicitWorldLayoutDoesNotCopyRootExecutables()
    {
        var root = CreateRoot("nativePC");
        WriteFile(root, "nativePC/pl/test.bin", "mod");
        WriteFile(root, "MonsterHunterWorld.exe", "must-not-deploy");
        var parsed = ModLayoutParser.Parse(GameProfile.Get(GameId.World), WriteFile(root, "source.tmp", ""), root);

        Assert.Single(parsed.Files);
        Assert.Equal("nativePC/pl/test.bin", parsed.Files[0].RelativeDest);
        Assert.DoesNotContain(parsed.Files, file => file.RelativeDest.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NestedPreviewNamedAssetIsNotDropped()
    {
        var root = CreateRoot("nativePC");
        WriteFile(root, "nativePC/ui/preview.png", "real-game-asset");
        var parsed = ModLayoutParser.Parse(GameProfile.Get(GameId.World), WriteFile(root, "source.tmp", ""), root);

        Assert.Contains(parsed.Files, file => file.RelativeDest == "nativePC/ui/preview.png");
    }

    [Fact]
    public void RootPreviewImageIsDetectedAndExcludedFromDeployFiles()
    {
        var root = CreateRoot("nativePC");
        WriteFile(root, "nativePC/pl/test.bin", "mod");
        WriteFile(root, "preview.png", "cover");
        var parsed = ModLayoutParser.Parse(GameProfile.Get(GameId.World), WriteFile(root, "source.tmp", ""), root);

        Assert.True(File.Exists(parsed.PreviewSource));
        Assert.Equal("preview.png", Path.GetFileName(parsed.PreviewSource), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(parsed.Files, file => Path.GetFileName(file.RelativeDest).Equals("preview.png", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RootCoverImageIsDetected()
    {
        var root = CreateRoot("nativePC");
        WriteFile(root, "nativePC/pl/test.bin", "mod");
        WriteFile(root, "Cover.png", "cover");
        var parsed = ModLayoutParser.Parse(GameProfile.Get(GameId.World), WriteFile(root, "source.tmp", ""), root);

        Assert.Equal("Cover.png", Path.GetFileName(parsed.PreviewSource), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void KnownWorldLoaderFilesAreAllowedButGameExecutablesAreNot()
    {
        var root = CreateRoot("nativePC");
        WriteFile(root, "nativePC/pl/test.bin", "mod");
        WriteFile(root, "dinput8.dll", "loader");
        WriteFile(root, "MonsterHunterWorld.exe", "blocked");
        var game = GameProfile.Get(GameId.World);
        var parsed = ModLayoutParser.Parse(game, WriteFile(root, "source.tmp", ""), root);

        Assert.Contains(parsed.Files, file => file.RelativeDest == "dinput8.dll");
        Assert.DoesNotContain(parsed.Files, file => file.RelativeDest.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        Assert.True(ModLayoutParser.IsSafeDeploymentPath(game, "dinput8.dll"));
    }

    [Theory]
    [InlineData(GameId.World, "MonsterHunterWorld.exe")]
    [InlineData(GameId.World, "../MonsterHunterWorld.exe")]
    [InlineData(GameId.Rise, "MonsterHunterRise.exe")]
    [InlineData(GameId.Wilds, "natives/../MonsterHunterWilds.exe")]
    [InlineData(GameId.World, "/nativePC/file.bin")]
    [InlineData(GameId.World, "\\nativePC\\file.bin")]
    public void UnsafeGamePathsAreRejected(GameId gameId, string path)
    {
        Assert.False(ModLayoutParser.IsSafeDeploymentPath(GameProfile.Get(gameId), path));
    }

    [Fact]
    public void ModuleConfigInstallsRequiredAndRecommendedDefaultFiles()
    {
        var root = CreateRoot("files", "variants");
        WriteFile(root, "files/base.bin", "base");
        WriteFile(root, "variants/red.bin", "red");
        WriteFile(root, "variants/blue.bin", "blue");
        WriteFile(root, "files/bonus.bin", "bonus");
        WriteFile(root, "ModuleConfig.xml", """
            <config>
              <moduleName>Configured Mod</moduleName>
              <requiredInstallFiles>
                <file source="files/base.bin" destination="nativePC/base.bin" />
              </requiredInstallFiles>
              <installSteps>
                <installStep name="Color">
                  <optionalFileGroups>
                    <group name="Color" type="SelectExactlyOne">
                      <plugins>
                        <plugin name="Red">
                          <files><file source="variants/red.bin" destination="nativePC/color.bin" /></files>
                          <conditionFlags><flag name="color">red</flag></conditionFlags>
                          <typeDescriptor><type name="Recommended" /></typeDescriptor>
                        </plugin>
                        <plugin name="Blue">
                          <files><file source="variants/blue.bin" destination="nativePC/color.bin" /></files>
                          <typeDescriptor><type name="Optional" /></typeDescriptor>
                        </plugin>
                      </plugins>
                    </group>
                  </optionalFileGroups>
                </installStep>
              </installSteps>
              <conditionalFileInstalls>
                <patterns>
                  <pattern>
                    <dependencies operator="And"><flagDependency flag="color" value="red" /></dependencies>
                    <files><file source="files/bonus.bin" destination="nativePC/bonus.bin" /></files>
                  </pattern>
                </patterns>
              </conditionalFileInstalls>
            </config>
            """);

        var parsed = ModLayoutParser.Parse(GameProfile.Get(GameId.World), WriteFile(root, "source.tmp", ""), root);

        Assert.Equal("Configured Mod", parsed.Name);
        Assert.Equal(3, parsed.Files.Count);
        Assert.Contains(parsed.Files, file => file.RelativeDest == "nativePC/base.bin");
        Assert.Contains(parsed.Files, file => file.RelativeDest == "nativePC/color.bin" && File.ReadAllText(file.SourcePath) == "red");
        Assert.Contains(parsed.Files, file => file.RelativeDest == "nativePC/bonus.bin");
    }

    [Fact]
    public void ModuleConfigBareModelPathIsMappedIntoNativePc()
    {
        var root = CreateRoot("files");
        WriteFile(root, "files/pl/armor/test.bin", "model");
        WriteFile(root, "ModuleConfig.xml", """
            <config>
              <moduleName>Bare Path</moduleName>
              <requiredInstallFiles>
                <folder source="files" destination="/" />
              </requiredInstallFiles>
            </config>
            """);

        var parsed = ModLayoutParser.Parse(GameProfile.Get(GameId.World), WriteFile(root, "source.tmp", ""), root);

        Assert.Equal("nativePC/pl/armor/test.bin", Assert.Single(parsed.Files).RelativeDest);
    }

    [Fact]
    public void ArchiveTraversalIsRejected()
    {
        var root = CreateRoot();
        var archive = Path.Combine(root, "bad.zip");
        using (var stream = File.Create(archive))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("../escape.bin");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("escape");
        }

        Assert.Throws<InvalidDataException>(() => ArchiveExtractor.Extract(archive, Path.Combine(root, "extract")));
        Assert.False(File.Exists(Path.Combine(root, "escape.bin")));
    }

    [Fact]
    public void BackupKeepsTheOriginalAcrossMultipleOverwrites()
    {
        var appId = Random.Shared.Next(8_000_000, 9_000_000);
        var game = new GameProfile(GameId.World, "test", "test", "test", "test.exe", appId, "test", "", 0);
        var gameRoot = CreateRoot("nativePC");
        var relative = "nativePC/test.bin";
        var destination = WriteFile(gameRoot, relative, "original");
        var source = WriteFile(gameRoot, "mod.bin", "first-mod");
        var backups = new BackupStore(game);

        backups.OnDeploy(relative, destination, source);
        File.WriteAllText(destination, "first-mod");
        backups.OnDeploy(relative, destination, source);
        File.WriteAllText(destination, "second-mod");
        File.Delete(destination);
        backups.OnRemove(destination, relative, keepBackup: false, source);

        Assert.Equal("original", File.ReadAllText(destination));
        if (Directory.Exists(AppPaths.GameDir(appId)))
        {
            Directory.Delete(AppPaths.GameDir(appId), true);
        }
    }

    [Fact]
    public void WildsPakAllocatorAvoidsExistingGamePatchNumbers()
    {
        var root = CreateRoot();
        WriteFile(root, "re_chunk_000.pak.sub_000.pak.patch_007.pak", "game-pak");
        var mod = new ModRecord { Id = 1001, Enabled = true, Files = ["source.pak"] };

        PakAllocator.Assign(GameProfile.Get(GameId.Wilds), root, [mod], mod, true);

        Assert.Equal("re_chunk_000.pak.sub_000.pak.patch_008.pak", mod.OverwriteFiles["source.pak"]);
    }

    [Fact]
    public void PakAllocatorFollowsTheHighestExistingGamePatch()
    {
        var root = CreateRoot();
        WriteFile(root, "re_chunk_000.pak", "base");
        WriteFile(root, "re_chunk_000.pak.sub_000.pak", "sub");
        WriteFile(root, "re_chunk_000.pak.sub_000.pak.patch_009.pak", "official");
        WriteFile(root, "re_chunk_000.pak.sub_000.pak.patch_010.pak", "official-next");
        var mod = new ModRecord { Id = 1001, Enabled = true, Files = ["source.pak"] };

        PakAllocator.Assign(GameProfile.Get(GameId.Wilds), root, [mod], mod, true);

        Assert.Equal("re_chunk_000.pak.sub_000.pak.patch_011.pak", mod.OverwriteFiles["source.pak"]);
    }

    [Fact]
    public void RisePakAllocatorDoesNotUseAFixedStartingNumber()
    {
        var root = CreateRoot();
        WriteFile(root, "re_chunk_000.pak.patch_003.pak", "official");
        var mod = new ModRecord { Id = 1001, Enabled = true, Files = ["source.pak"] };

        PakAllocator.Assign(GameProfile.Get(GameId.Rise), root, [mod], mod, true);

        Assert.Equal("re_chunk_000.pak.patch_004.pak", mod.OverwriteFiles["source.pak"]);
    }

    [Fact]
    public void RawPakNameIsAcceptedForStorageAndMappedBeforeDeployment()
    {
        var root = CreateRoot();
        WriteFile(root, "visual-overhaul.pak", "pak-content");
        var game = GameProfile.Get(GameId.Rise);
        var parsed = ModLayoutParser.Parse(game, WriteFile(root, "source.tmp", ""), root);
        var item = Assert.Single(parsed.Files);
        var mod = new ModRecord { Id = 1001, Enabled = true, Files = [item.RelativeDest] };

        PakAllocator.Assign(game, root, [mod], mod, true);

        Assert.Equal("visual-overhaul.pak", item.RelativeDest);
        Assert.True(ModLayoutParser.IsSafeStoredPath(item.RelativeDest));
        Assert.Equal("re_chunk_000.pak.patch_001.pak", mod.DeployPath(item.RelativeDest));
        Assert.True(ModLayoutParser.IsSafeDeploymentPath(game, mod.DeployPath(item.RelativeDest)));
    }

    [Fact]
    public void ExistingReFrameworkLayoutDoesNotDuplicateAutorunPath()
    {
        var root = CreateRoot("reframework/autorun");
        WriteFile(root, "reframework/autorun/script.lua", "script");
        var parsed = ModLayoutParser.Parse(GameProfile.Get(GameId.Rise), WriteFile(root, "source.tmp", ""), root);

        var item = Assert.Single(parsed.Files);
        Assert.Equal("reframework/autorun/script.lua", item.RelativeDest);
    }

    [Fact]
    public void StandalonePluginKeepsItsCompanionConfiguration()
    {
        var root = CreateRoot();
        WriteFile(root, "plugin.dll", "plugin");
        WriteFile(root, "plugin.json", "config");
        var parsed = ModLayoutParser.Parse(GameProfile.Get(GameId.Rise), WriteFile(root, "source.tmp", ""), root);

        Assert.Equal(2, parsed.Files.Count);
        Assert.Contains(parsed.Files, file => file.RelativeDest == "reframework/plugins/plugin.dll");
        Assert.Contains(parsed.Files, file => file.RelativeDest == "reframework/plugins/plugin.json");
    }

    [Fact]
    public void KnownRootDllReplacementKeepsItsCompanionConfigurationAtGameRoot()
    {
        var root = CreateRoot();
        WriteFile(root, "nvngx_dlss.dll", "replacement");
        WriteFile(root, "nvngx.ini", "config");
        var parsed = ModLayoutParser.Parse(GameProfile.Get(GameId.Wilds), WriteFile(root, "source.tmp", ""), root);

        Assert.Contains(parsed.Files, file => file.RelativeDest == "nvngx_dlss.dll");
        Assert.Contains(parsed.Files, file => file.RelativeDest == "nvngx.ini");
    }

    private string CreateRoot(params string[] dirs)
    {
        var root = Path.Combine(Path.GetTempPath(), "mh-mod-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        foreach (var dir in dirs)
        {
            Directory.CreateDirectory(Path.Combine(root, dir));
        }

        return root;
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
        foreach (var root in _tempRoots)
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
