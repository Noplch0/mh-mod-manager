namespace MhModManager.Core.Equipment;

/// <summary>
/// 读取 RE Engine pak 补丁的条目表，把条目 hash 映射回游戏内路径。
/// 只读文件表，不解压数据。版本 2.0 / 4.0 / 4.1，feature 0 / 8 / 24 均支持。
/// </summary>
internal static class PakReader
{
    private const uint Magic = 0x414B504B; // "PKPA"
    private const int MaxPakSize = 0x6400000; // 100 MB，原版同样限制

    public static List<string> ListInternalPaths(string pakPath, PakFileIndex index)
    {
        var result = new List<string>();
        if (!TryOpenTable(pakPath, out var table))
        {
            return result;
        }

        using (table)
        {
            foreach (var hash in table.Hashes())
            {
                var path = index.GetPath(hash);
                if (path is not null)
                {
                    result.Add(path);
                }
            }
        }

        return result;
    }

    public static int RewritePaths(string pakPath, PakFileIndex index, Func<string, string?> map, Func<string, byte[]?>? contentPatch = null)
    {
        if (!TryOpenTable(pakPath, out var table))
        {
            return 0;
        }

        using (table)
        {
            var changed = 0;
            var pending = new List<(int Entry, byte[] Payload)>();
            for (var i = 0; i < table.TotalFiles; i++)
            {
                var hash = table.ReadHash(i);
                var raw = index.GetRawPath(hash);
                if (raw is null)
                {
                    continue;
                }

                var normalized = raw.Replace('\\', '/');
                var entryChanged = false;
                if (contentPatch is not null && table.SupportsContentPatch)
                {
                    var payload = contentPatch(normalized);
                    if (payload is not null)
                    {
                        pending.Add((i, payload));
                        entryChanged = true;
                    }
                }

                var next = map(normalized);
                if (!string.IsNullOrWhiteSpace(next) && !next.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                {
                    if (raw.Contains('\\'))
                    {
                        next = next.Replace('/', '\\');
                    }

                    var newHash = PakHash.PathHash(next);
                    if (newHash != hash)
                    {
                        table.WriteHash(i, newHash);
                        entryChanged = true;
                    }
                }

                if (entryChanged)
                {
                    changed++;
                }
            }

            if (pending.Count > 0)
            {
                AppendPayloads(pakPath, table, pending);
            }

            if (changed > 0)
            {
                table.Save();
            }

            return changed;
        }
    }

    /// <summary>
    /// avp 等内容替换采用“追加式”：新数据以未压缩条目写到文件末尾，
    /// 只更新该条目的 offset/size 字段，其余条目与数据零改动。
    /// 已用真实 MOD pak 实证 v4 条目布局：hash@0、offset@8、uncompressedSize@16、compressedSize@24（@32 起未用），
    /// 且 MOD pak 的条目全部是未压缩存储（compressedSize == uncompressedSize）。
    /// </summary>
    private static void AppendPayloads(string pakPath, PakTable table, List<(int Entry, byte[] Payload)> pending)
    {
        using var stream = new FileStream(pakPath, FileMode.Open, FileAccess.Write, FileShare.Read);
        stream.Seek(0, SeekOrigin.End);
        foreach (var (entry, payload) in pending)
        {
            var offset = stream.Position;
            stream.Write(payload, 0, payload.Length);
            table.WriteLocation(entry, offset, payload.Length, payload.Length);
        }
    }

    private static bool TryOpenTable(string pakPath, out PakTable table)
    {
        table = null!;
        var info = new FileInfo(pakPath);
        if (!info.Exists || info.Length <= 16 || info.Length > MaxPakSize)
        {
            return false;
        }

        using var fs = File.OpenRead(pakPath);
        using var br = new BinaryReader(fs);
        if (br.ReadUInt32() != Magic)
        {
            return false;
        }

        var major = br.ReadByte();
        var minor = br.ReadByte();
        var feature = br.ReadInt16();
        var totalFiles = br.ReadInt32();
        _ = br.ReadUInt32();
        if (totalFiles <= 0 || totalFiles > 1_000_000)
        {
            return false;
        }

        var entrySize = major switch
        {
            2 => 24,
            4 => 48,
            _ => 0
        };
        if (entrySize == 0 || (major == 4 && minor != 0 && minor != 1))
        {
            return false;
        }

        byte[]? extra = null;
        if (feature == 24)
        {
            extra = br.ReadBytes(4);
            if (extra.Length != 4)
            {
                return false;
            }
        }

        byte[]? key = null;
        byte[] tableBytes;
        var tableLength = totalFiles * entrySize;
        if (feature is 8 or 24)
        {
            key = br.ReadBytes(0x80);
            var encrypted = br.ReadBytes(tableLength);
            if (key.Length != 0x80 || encrypted.Length != tableLength)
            {
                return false;
            }

            tableBytes = XorTable(encrypted, key);
        }
        else if (feature == 0)
        {
            tableBytes = br.ReadBytes(tableLength);
            if (tableBytes.Length != tableLength)
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        table = new PakTable(pakPath, major, feature, totalFiles, entrySize, extra, key, tableBytes);
        return true;
    }

    private static byte[] XorTable(byte[] data, byte[] key)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            var x = (byte)(i + key[i % 32] * key[i % 29]);
            result[i] = (byte)(data[i] ^ x);
        }

        return result;
    }

    private sealed class PakTable : IDisposable
    {
        private readonly string _path;
        private readonly byte _major;
        private readonly short _feature;
        private readonly int _entrySize;
        private readonly byte[]? _extra;
        private readonly byte[]? _key;
        private readonly byte[] _table;
        private bool _dirty;

        public PakTable(string path, byte major, short feature, int totalFiles, int entrySize, byte[]? extra, byte[]? key, byte[] table)
        {
            _path = path;
            _major = major;
            _feature = feature;
            TotalFiles = totalFiles;
            _entrySize = entrySize;
            _extra = extra;
            _key = key;
            _table = table;
        }

        public int TotalFiles { get; }

        /// <summary>只有 v4 的条目位置字段经过实证，v2 仅支持路径哈希改写。</summary>
        public bool SupportsContentPatch => _major == 4;

        public void WriteLocation(int index, long location, long uncompressedSize, long compressedSize)
        {
            var entryOffset = index * _entrySize;
            WriteUInt64(entryOffset + 8, (ulong)location);
            WriteUInt64(entryOffset + 16, (ulong)uncompressedSize);
            WriteUInt64(entryOffset + 24, (ulong)compressedSize);
            _dirty = true;
        }

        public IEnumerable<ulong> Hashes()
        {
            for (var i = 0; i < TotalFiles; i++)
            {
                yield return ReadHash(i);
            }
        }

        public ulong ReadHash(int index)
        {
            var offset = index * _entrySize;
            if (_major == 2)
            {
                var lower = BitConverter.ToUInt32(_table, offset + 16);
                var upper = BitConverter.ToUInt32(_table, offset + 20);
                return ((ulong)upper << 32) | lower;
            }

            var hashLower = BitConverter.ToUInt32(_table, offset);
            var hashUpper = BitConverter.ToUInt32(_table, offset + 4);
            return ((ulong)hashUpper << 32) | hashLower;
        }

        public void WriteHash(int index, ulong hash)
        {
            var offset = index * _entrySize;
            var lower = (uint)hash;
            var upper = (uint)(hash >> 32);
            if (_major == 2)
            {
                WriteUInt32(offset + 16, lower);
                WriteUInt32(offset + 20, upper);
            }
            else
            {
                WriteUInt32(offset, lower);
                WriteUInt32(offset + 4, upper);
            }

            _dirty = true;
        }

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            var payload = _key is null ? _table : XorTable(_table, _key);
            using var fs = new FileStream(_path, FileMode.Open, FileAccess.Write, FileShare.Read);
            fs.Seek(16, SeekOrigin.Begin);
            if (_feature == 24)
            {
                fs.Write(_extra ?? new byte[4]);
            }

            if (_key is not null)
            {
                fs.Write(_key);
            }

            fs.Write(payload);
        }

        public void Dispose()
        {
        }

        private void WriteUInt32(int offset, uint value)
        {
            _table[offset] = (byte)value;
            _table[offset + 1] = (byte)(value >> 8);
            _table[offset + 2] = (byte)(value >> 16);
            _table[offset + 3] = (byte)(value >> 24);
        }

        private void WriteUInt64(int offset, ulong value)
        {
            for (var i = 0; i < 8; i++)
            {
                _table[offset + i] = (byte)(value >> (8 * i));
            }
        }
    }
}
