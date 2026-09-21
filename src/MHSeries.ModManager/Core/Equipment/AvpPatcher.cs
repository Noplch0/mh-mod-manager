using MhModManager.Models;

namespace MhModManager.Core.Equipment;

/// <summary>
/// 对齐原版 AvpFixer：_avp.user.3 的内容按目标防具 ID 重写，否则改了文件名游戏内也不生效。
/// 男款直接使用内置模板（原版即如此，内容与 ID 无关）；
/// 女款以模板为基础，在固定偏移写入新 ID 的 ASCII 数字（原版 RenameAvp 的写入序列）。
/// </summary>
internal static class AvpPatcher
{
    private const string FemaleResource = "MhModManager.Data.Equipment.mhws_female_avp.user.bin";
    private const string MaleResource = "MhModManager.Data.Equipment.mhws_male_avp.user.bin";

    // 模板中 4 条记录的基址（已用模板自带 ID 004_001 逐字节验证）：
    // 一级三位在基址 +0/+2/+4，二级三位在基址 +8/+10/+12，其余为间隔字节。
    private static readonly int[] RecordBases = [0x98, 0xA8, 0x288, 0x298];

    private static byte[]? _female;
    private static byte[]? _male;

    public static bool IsAvpFile(string path) =>
        path.Replace('\\', '/').EndsWith("_avp.user.3", StringComparison.OrdinalIgnoreCase);

    /// <summary>返回目标装备的 avp 内容；非荒野防具或模板缺失时返回 null（保持原内容）。</summary>
    public static byte[]? Build(GameId game, EquipKind kind, int toId)
    {
        if (game != GameId.Wilds || !EquipKindInfo.IsArmor(kind))
        {
            return null;
        }

        var bytes = kind == EquipKind.FemaleArmor ? LoadFemale() : LoadMale();
        if (bytes is null)
        {
            return null;
        }

        var copy = (byte[])bytes.Clone();
        if (kind == EquipKind.MaleArmor)
        {
            return copy;
        }

        var first = toId / 1_000;
        var second = toId % 1_000;
        foreach (var baseOffset in RecordBases)
        {
            WriteDigits(copy, baseOffset, first);
            WriteDigits(copy, baseOffset + 8, second);
        }

        return copy;
    }

    private static void WriteDigits(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)('0' + value / 100 % 10);
        bytes[offset + 2] = (byte)('0' + value / 10 % 10);
        bytes[offset + 4] = (byte)('0' + value % 10);
    }

    private static byte[]? LoadFemale() => _female ??= Load(FemaleResource);

    private static byte[]? LoadMale() => _male ??= Load(MaleResource);

    private static byte[]? Load(string name)
    {
        using var stream = typeof(AvpPatcher).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
