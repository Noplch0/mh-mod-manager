using MhModManager.Core.Equipment;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

public sealed class PakReaderTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "mh-mod-manager-pak-tests", Guid.NewGuid().ToString("N"));

    public PakReaderTests()
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
    public void WildsIndexMapsInternalPathsBackToNames()
    {
        var index = PakFileIndex.For(GameId.Wilds);
        Assert.True(index.Count > 40_000);

        var known = "natives/STM/Art/Model/Character/ch03/001/001/1/ch03_001_0011.mesh.241111606";
        var hash = PakHash.PathHash(known);
        var path = index.GetPath(hash);
        Assert.Equal(known, path);
    }

    [Fact]
    public void RiseIndexUsesBackslashSeparatedList()
    {
        var index = PakFileIndex.For(GameId.Rise);
        Assert.True(index.Count > 20_000);

        var known = "natives\\STM\\player\\mod\\f\\pl001\\f_body001.mesh.2109148288";
        var hash = PakHash.PathHash(known);
        var path = index.GetPath(hash);
        Assert.Equal(known.Replace('\\', '/'), path);
    }

    [Fact]
    public void PakV4EntryTableResolvesInternalFileNames()
    {
        var index = PakFileIndex.For(GameId.Wilds);
        var internalPath = "natives/STM/Art/Model/Character/ch03/001/001/1/ch03_001_0011.mesh.241111606";
        var hash = PakHash.PathHash(internalPath);

        var pak = Path.Combine(_tmp, "test.pak");
        using (var fs = File.Create(pak))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(0x414B504Bu); // magic
            bw.Write((byte)4); // major
            bw.Write((byte)0); // minor
            bw.Write((short)0); // feature
            bw.Write(2); // total files
            bw.Write(0u); // fingerprint
            // entry 1: a known model path
            WriteV4Entry(bw, hash);
            // entry 2: unknown hash (should be skipped)
            WriteV4Entry(bw, 0xDEADBEEFCAFEBABEul);
        }

        var files = PakReader.ListInternalPaths(pak, index);
        var hit = Assert.Single(files);
        Assert.Equal(internalPath, hit);
    }

    [Fact]
    public void PakV2EntryTableIsSupported()
    {
        var index = PakFileIndex.For(GameId.Wilds);
        var internalPath = "natives/STM/Art/Model/Character/ch03/001/001/1/ch03_001_0011.chain2.13";
        var hash = PakHash.PathHash(internalPath);

        var pak = Path.Combine(_tmp, "test2.pak");
        using (var fs = File.Create(pak))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(0x414B504Bu);
            bw.Write((byte)2); // major
            bw.Write((byte)0); // minor
            bw.Write((short)0);
            bw.Write(1);
            bw.Write(0u);
            // v2 entry: offset(8) + decompressedSize(8) + hashLower(4) + hashUpper(4)
            bw.Write(0L);
            bw.Write(0L);
            bw.Write((uint)(hash & 0xFFFFFFFF));
            bw.Write((uint)(hash >> 32));
        }

        var files = PakReader.ListInternalPaths(pak, index);
        Assert.Equal(internalPath, Assert.Single(files));
    }

    [Fact]
    public void EndToEndPakModResolvesEquipmentNames()
    {
        var index = PakFileIndex.For(GameId.Wilds);
        // 希望α(女款) 的模型网格路径（真实存在于清单中）
        var internalPath = "natives/STM/Art/Model/Character/ch03/001/001/1/ch03_001_0011.mesh.241111606";
        var hash = PakHash.PathHash(internalPath);

        var pak = Path.Combine(_tmp, "armor.pak");
        using (var fs = File.Create(pak))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(0x414B504Bu);
            bw.Write((byte)4);
            bw.Write((byte)0);
            bw.Write((short)0);
            bw.Write(1);
            bw.Write(0u);
            WriteV4Entry(bw, hash);
        }

        var hits = EquipmentResolver.Resolve(GameId.Wilds, ["armor.pak"], _tmp);

        var hit = Assert.Single(hits);
        Assert.Equal("女装备", hit.Type);
        Assert.Equal("希望α(女款)", hit.Name);
    }

    private static void WriteV4Entry(BinaryWriter bw, ulong hash)
    {
        bw.Write((uint)(hash & 0xFFFFFFFF)); // hashLower
        bw.Write((uint)(hash >> 32)); // hashUpper
        bw.Write(0L); // offset
        bw.Write(0L); // compressed size
        bw.Write(0L); // decompressed size
        bw.Write(0L); // attributes
        bw.Write(0UL); // checksum
    }
}
