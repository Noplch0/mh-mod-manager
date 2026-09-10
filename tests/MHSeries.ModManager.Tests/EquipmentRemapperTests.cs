using MhModManager.Core.Equipment;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

public sealed class EquipmentRemapperTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "mh-mod-manager-remap-tests", Guid.NewGuid().ToString("N"));

    public EquipmentRemapperTests()
    {
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tmp, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void WorldArmorFolderAndFileNameAreRewritten()
    {
        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.World, EquipKind.FemaleArmor, 10_000, 20_000,
            @"nativePC\pl\f_equip\pl001_0000\mod\f_body001.mod3",
            out var shortName));
        Assert.Equal(@"nativepc\pl\f_equip\pl002_0000\mod\f_body002.mod3", shortName);

        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.World, EquipKind.FemaleArmor, 840_000, 10_001,
            @"nativePC\pl\f_equip\pl084_0000\body\mod\f_body084_0000.mod3",
            out var fullName));
        Assert.Equal(@"nativepc\pl\f_equip\pl001_0001\body\mod\f_body001_0001.mod3", fullName);
        Assert.Equal(10_001, Assert.Single(EquipmentResolver.Resolve(GameId.World, [fullName])).Id);
    }

    [Fact]
    public void WorldArmorMismatchedFileNameIsStillRewritten()
    {
        const string mixed = @"nativePC\pl\f_equip\pl001_0000\body\mod\f_body108_0000.mod3";
        Assert.Equal(1_080_000, Assert.Single(EquipmentResolver.Resolve(GameId.World, [mixed])).Id);

        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.World, EquipKind.FemaleArmor, 1_080_000, 10_000, mixed, out var fromFile));
        Assert.Equal(@"nativepc\pl\f_equip\pl001_0000\body\mod\f_body001_0000.mod3", fromFile);

        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.World, EquipKind.FemaleArmor, 10_000, 10_000, mixed, out var repair));
        Assert.Equal(@"nativepc\pl\f_equip\pl001_0000\body\mod\f_body001_0000.mod3", repair);
    }

    [Fact]
    public void WildsArmorAndPrefabTokensRewriteTogether()
    {
        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.Wilds, EquipKind.FemaleArmor, 1_001, 2_001,
            "natives/STM/Art/Model/Character/ch03/001/001/1/ch03_001_0011.mesh.241111606",
            out var mesh));
        Assert.Contains("/ch03/002/001/", mesh.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Contains("ch03_002_0011", mesh, StringComparison.Ordinal);

        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.Wilds, EquipKind.FemaleArmor, 1_001, 2_001,
            "natives/gamedesign/equip/_prefab/armor/female/001/001/armor.pfb.17",
            out var pfb));
        Assert.Contains("/female/002/001/", pfb.Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Fact]
    public void RiseArmorFileNameIsRewrittenWithFolder()
    {
        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.Rise, EquipKind.FemaleArmor, 1, 44,
            "natives/stm/player/mod/f/pl001/f_body001.mesh.2109148288",
            out var next));
        Assert.Equal("natives/stm/player/mod/f/pl044/f_body044.mesh.2109148288", next);
        Assert.Equal(44, Assert.Single(EquipmentResolver.Resolve(GameId.Rise, [next])).Id);
    }

    [Fact]
    public void RiseWeaponCodeIsRewritten()
    {
        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.Rise, EquipKind.LongSword, 1, 44,
            "natives/weapon/G_Swd/G_Swd001/G_Swd001.mesh",
            out var next));
        Assert.Contains("g_swd044/g_swd044", next, StringComparison.Ordinal);
        Assert.Equal(44, Assert.Single(EquipmentResolver.Resolve(GameId.Rise, [next])).Id);
    }

    [Fact]
    public void LooseFilesAreMovedToTargetSuit()
    {
        var relative = "natives/art/model/character/ch03/001/001/body.mesh";
        var source = Path.Combine(_tmp, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "mesh");

        var changed = EquipmentRemapper.Apply(GameId.Wilds, _tmp, [relative], EquipKind.FemaleArmor, 1_001, 2_001, isPfb: false, withTex: false);
        Assert.Equal(1, changed);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(_tmp, "natives", "art", "model", "character", "ch03", "002", "001", "body.mesh")));
    }

    [Fact]
    public void WorldArmorLooseFilesRenameFolderAndFile()
    {
        var relative = @"nativePC\pl\f_equip\pl084_0000\body\mod\f_body084_0000.mod3";
        var source = Path.Combine(_tmp, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "mod3");

        var changed = EquipmentRemapper.Apply(
            GameId.World, _tmp, [relative], EquipKind.FemaleArmor, 840_000, 10_000, isPfb: false, withTex: false);
        Assert.Equal(1, changed);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(Path.Combine(
            _tmp, "nativePC", "pl", "f_equip", "pl001_0000", "body", "mod", "f_body001_0000.mod3")));
    }

    [Fact]
    public void PakHashesAreRewrittenToTargetSuit()
    {
        var fromPath = "natives/STM/Art/Model/Character/ch03/001/001/1/ch03_001_0011.mesh.241111606";
        var pak = Path.Combine(_tmp, "armor.pak");
        WriteV4Pak(pak, PakHash.PathHash(fromPath));

        var changed = EquipmentRemapper.Apply(GameId.Wilds, _tmp, ["armor.pak"], EquipKind.FemaleArmor, 1_001, 2_001, isPfb: false, withTex: false);
        Assert.Equal(1, changed);

        var hits = EquipmentResolver.Resolve(GameId.Wilds, ["armor.pak"], _tmp);
        var hit = Assert.Single(hits);
        Assert.Equal(2_001, hit.Id);
        Assert.Equal("女装备", hit.Type);
    }

    [Fact]
    public void TextureFilesStayUnlessRequested()
    {
        var relative = "natives/art/model/character/ch03/001/001/body.tex.1";
        var source = Path.Combine(_tmp, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, "tex");

        var skipped = EquipmentRemapper.Apply(GameId.Wilds, _tmp, [relative], EquipKind.FemaleArmor, 1_001, 2_001, isPfb: false, withTex: false);
        Assert.Equal(0, skipped);
        Assert.True(File.Exists(source));

        var changed = EquipmentRemapper.Apply(GameId.Wilds, _tmp, [relative], EquipKind.FemaleArmor, 1_001, 2_001, isPfb: false, withTex: true);
        Assert.Equal(1, changed);
    }

    private static void WriteV4Pak(string path, ulong hash)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write(0x414B504Bu);
        bw.Write((byte)4);
        bw.Write((byte)0);
        bw.Write((short)0);
        bw.Write(1);
        bw.Write(0u);
        bw.Write((uint)(hash & 0xFFFFFFFF));
        bw.Write((uint)(hash >> 32));
        bw.Write(0L);
        bw.Write(0L);
        bw.Write(0L);
        bw.Write(0L);
        bw.Write(0UL);
    }
}
