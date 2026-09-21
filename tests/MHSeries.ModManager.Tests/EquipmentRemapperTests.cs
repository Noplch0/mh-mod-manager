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

    [Fact]
    public void WildsBonesystemFileIsRenamed()
    {
        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.Wilds, EquipKind.MaleArmor, 0, 1,
            "reframework/data/bonesystem/ch02_000_0001.lua", out var bone));
        Assert.Equal("reframework/data/bonesystem/ch02_000_0011.lua", bone);
    }

    [Fact]
    public void RiseBonesystemFileIsRenamed()
    {
        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.Rise, EquipKind.MaleArmor, 0, 44,
            "reframework/data/bonesystem/m_body000.lua", out var bone));
        Assert.Equal("reframework/data/bonesystem/m_body044.lua", bone);
    }

    [Fact]
    public void WildsLayeredTextureParityIsRewritten()
    {
        // 0→2：偶偶/奇奇配对，层叠纹理 001 跟随移动到 003。
        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.Wilds, EquipKind.MaleArmor, 0, 2,
            "natives/art/model/character/ch02/000/000/1/textures/ch02_000_001.tex",
            out var layered));
        Assert.Equal("natives/art/model/character/ch02/000/002/1/textures/ch02_000_003.tex", layered);

        // 0→1 为同一层叠对：文件名保持不变，仅目录移动（与原版位置配对语义一致）。
        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.Wilds, EquipKind.MaleArmor, 0, 1,
            "natives/art/model/character/ch02/000/000/1/textures/ch02_000_001.tex",
            out var adjacent));
        Assert.Equal("natives/art/model/character/ch02/000/001/1/textures/ch02_000_001.tex", adjacent);
    }

    [Fact]
    public void WildsCrossGenderPartPrefixIsRewritten()
    {
        Assert.True(EquipmentPathRewriter.TryRewrite(
            GameId.Wilds, EquipKind.MaleArmor, 0, 2,
            "natives/gamedesign/equip/_prefab/armor/male/000/000/arm/ch03_000_0002.mesh",
            out var next));
        Assert.Equal("natives/gamedesign/equip/_prefab/armor/male/000/002/arm/ch03_000_0022.mesh", next);
    }

    [Fact]
    public void LooseAvpContentIsRebuiltForTargetSuit()
    {
        // 男款：整体写内置模板。
        var male = "natives/gamedesign/equip/_prefab/armor/male/000/000/000_000_avp.user.3";
        WriteTemp(male, [1, 2, 3]);
        var changed = EquipmentRemapper.Apply(GameId.Wilds, _tmp, [male], EquipKind.MaleArmor, 0, 2, isPfb: false, withTex: false);
        Assert.Equal(2, changed);
        var maleDest = Path.Combine(_tmp, "natives", "gamedesign", "equip", "_prefab", "armor", "male", "000", "002", "000_002_avp.user.3");
        Assert.True(File.Exists(maleDest));
        Assert.Equal(ReadTemplate("MhModManager.Data.Equipment.mhws_male_avp.user.bin"), File.ReadAllBytes(maleDest));

        // 女款：模板 + 固定偏移写目标 ID 的 ASCII 数字，其余字节不变。
        var female = "natives/gamedesign/equip/_prefab/armor/female/000/000/000_000_avp.user.3";
        WriteTemp(female, [9, 8, 7]);
        changed = EquipmentRemapper.Apply(GameId.Wilds, _tmp, [female], EquipKind.FemaleArmor, 0, 2, isPfb: false, withTex: false);
        Assert.Equal(2, changed);
        var femaleDest = Path.Combine(_tmp, "natives", "gamedesign", "equip", "_prefab", "armor", "female", "000", "002", "000_002_avp.user.3");
        Assert.True(File.Exists(femaleDest));
        var template = ReadTemplate("MhModManager.Data.Equipment.mhws_female_avp.user.bin");
        var content = File.ReadAllBytes(femaleDest);
        Assert.Equal(template.Length, content.Length);
        var patched = new HashSet<int>();
        foreach (var baseOffset in new[] { 0x98, 0xA8, 0x288, 0x298 })
        {
            foreach (var delta in new[] { 0, 2, 4, 8, 10, 12 })
            {
                patched.Add(baseOffset + delta);
            }
        }

        for (var i = 0; i < template.Length; i++)
        {
            if (patched.Contains(i))
            {
                continue;
            }

            Assert.Equal(template[i], content[i]);
        }

        Assert.Equal((byte)'0', content[0x98]);
        Assert.Equal((byte)'0', content[0x9C]);
        Assert.Equal((byte)'0', content[0xA0]);
        Assert.Equal((byte)'0', content[0xA2]);
        Assert.Equal((byte)'2', content[0xA4]);
        Assert.Equal((byte)'0', content[0x288]);
        Assert.Equal((byte)'0', content[0x28C]);
        Assert.Equal((byte)'2', content[0x294]);
    }

    [Fact]
    public void PakAvpEntryIsRebuiltForTargetSuit()
    {
        var avpPath = "natives/STM/GameDesign/Equip/_Prefab/Armor/Male/000/000/000_000_avp.user.3";
        var unrelatedPath = "natives/STM/Art/Model/Character/ch02/000/900/PL_m_bodyEdit.motbank.4";
        var pak = Path.Combine(_tmp, "armor.pak");
        File.WriteAllBytes(pak, BuildV4Pak(
            (PakHash.PathHash(avpPath), [1, 2, 3, 4]),
            (PakHash.PathHash(unrelatedPath), [9, 9])));
        var before = File.ReadAllBytes(pak);

        var changed = EquipmentRemapper.Apply(GameId.Wilds, _tmp, ["armor.pak"], EquipKind.MaleArmor, 0, 2, isPfb: false, withTex: false);
        // pak 流程按条目计数：avp 条目的改名 + 内容替换合计 1 个条目。
        Assert.Equal(1, changed);

        var after = File.ReadAllBytes(pak);
        var template = ReadTemplate("MhModManager.Data.Equipment.mhws_male_avp.user.bin");
        var renamedAvp = "natives/stm/gamedesign/equip/_prefab/armor/male/000/002/000_002_avp.user.3";

        // 条目 0：哈希指向新路径，size 指向追加的模板内容。
        Assert.Equal(PakHash.PathHash(renamedAvp), BitConverter.ToUInt64(after, 16));
        Assert.Equal(before.Length, BitConverter.ToInt64(after, 16 + 8));
        Assert.Equal(template.Length, BitConverter.ToInt64(after, 16 + 16));
        Assert.Equal(template.Length, BitConverter.ToInt64(after, 16 + 24));
        Assert.Equal(template, after[^template.Length..]);

        // 条目 1（不含防具 ID 的文件）完全不动。
        Assert.Equal(before.Skip(16 + 48).Take(48), after.Skip(16 + 48).Take(48));
        Assert.Equal(PakHash.PathHash(unrelatedPath), BitConverter.ToUInt64(after, 16 + 48));
    }

    [Fact]
    public void RealWildsPakAcceptsLocationPatch()
    {
        var source = @"D:\tools\mh-modmanager\data\games\2246340\mods\1029\files\re_chunk_000.pak.sub_000.pak.patch_012.pak";
        if (!File.Exists(source))
        {
            return; // 无真实 pak 的环境跳过
        }

        var pak = Path.Combine(_tmp, "real.pak");
        File.Copy(source, pak, true);
        var before = File.ReadAllBytes(pak);
        var index = PakFileIndex.For(GameId.Wilds);
        var firstPath = Assert.Single(PakReader.ListInternalPaths(pak, index).Take(1));
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var changed = PakReader.RewritePaths(pak, index, _ => null, p => p == firstPath ? payload : null);
        Assert.Equal(1, changed);

        var after = File.ReadAllBytes(pak);
        Assert.Equal(before.Length + payload.Length, after.Length);
        Assert.Equal(payload, after[^payload.Length..]);

        var entryCount = BitConverter.ToInt32(after, 8);
        var patchedEntries = 0;
        for (var i = 0; i < entryCount; i++)
        {
            var entry = 16 + i * 48;
            var offset = BitConverter.ToInt64(after, entry + 8);
            var size = BitConverter.ToInt64(after, entry + 16);
            if (size == payload.Length)
            {
                patchedEntries++;
                Assert.Equal(after.Length - payload.Length, offset);
                Assert.Equal(payload, after[(int)offset..(int)(offset + size)]);
            }
            else
            {
                for (var j = 0; j < 48; j++)
                {
                    Assert.Equal(before[entry + j], after[entry + j]);
                }
            }
        }

        Assert.Equal(1, patchedEntries);
        // 表仍可解析且条目数不变。
        Assert.Equal(entryCount, PakReader.ListInternalPaths(pak, index).Count);
    }

    private string WriteTemp(string relative, byte[] content)
    {
        var path = Path.Combine(_tmp, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] ReadTemplate(string name)
    {
        using var stream = typeof(AvpPatcher).Assembly.GetManifestResourceStream(name)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static byte[] BuildV4Pak(params (ulong Hash, byte[] Payload)[] entries)
    {
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);
        writer.Write(0x414B504Bu);
        writer.Write((byte)4);
        writer.Write((byte)0);
        writer.Write((short)0);
        writer.Write(entries.Length);
        writer.Write(0u);
        long offset = 16 + entries.Length * 48;
        foreach (var (hash, payload) in entries)
        {
            writer.Write((uint)(hash & 0xFFFFFFFF));
            writer.Write((uint)(hash >> 32));
            writer.Write(offset);
            writer.Write((long)payload.Length);
            writer.Write((long)payload.Length);
            writer.Write(0L);
            writer.Write(0UL);
            offset += payload.Length;
        }

        foreach (var (_, payload) in entries)
        {
            writer.Write(payload);
        }

        return memory.ToArray();
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
