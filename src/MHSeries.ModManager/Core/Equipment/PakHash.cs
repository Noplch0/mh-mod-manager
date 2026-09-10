using System.Text;

namespace MhModManager.Core.Equipment;

/// <summary>
/// RE Engine pak 文件内部路径的 Murmur3 x86-32 哈希。
/// 原版逻辑取自 sample.exe 内嵌的 REE.Tool：GetStringHash = Murmur3(UTF-16LE bytes, seed 0xFFFFFFFF)，
/// 文件清单条目按 ToLower/ToUpper 各算一次拼成 64 位。
/// </summary>
internal static class PakHash
{
    private const uint C1 = 0xcc9e2d51;
    private const uint C2 = 0x1b873593;

    public static uint GetStringHash(string value, uint seed = 0xFFFFFFFF)
    {
        var bytes = Encoding.Unicode.GetBytes(value); // UTF-16LE
        var h = seed;
        var length = bytes.Length;

        var index = 0;
        while (index + 4 <= length)
        {
            var k = (uint)(bytes[index]
                           | bytes[index + 1] << 8
                           | bytes[index + 2] << 16
                           | bytes[index + 3] << 24);
            k *= C1;
            k = Rotl32(k, 15);
            k *= C2;
            h ^= k;
            h = Rotl32(h, 13);
            h = h * 5 + 0xe6546b64;
            index += 4;
        }

        var remaining = length - index;
        if (remaining > 0)
        {
            var k = (uint)bytes[index];
            if (remaining >= 2)
            {
                k |= (uint)bytes[index + 1] << 8;
            }

            if (remaining >= 3)
            {
                k |= (uint)bytes[index + 2] << 16;
            }

            k *= C1;
            k = Rotl32(k, 15);
            k *= C2;
            h ^= k;
        }

        h ^= (uint)length;
        h ^= h >> 16;
        h *= 0x85ebca6b;
        h ^= h >> 13;
        h *= 0xc2b2ae35;
        h ^= h >> 16;
        return h;
    }

    public static ulong PathHash(string path)
    {
        var lower = GetStringHash(path.ToLowerInvariant());
        var upper = GetStringHash(path.ToUpperInvariant());
        return ((ulong)upper << 32) | lower;
    }

    private static uint Rotl32(uint x, int r)
    {
        r &= 31;
        return (x << r) | (x >> (32 - r));
    }
}
