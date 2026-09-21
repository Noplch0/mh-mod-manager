using System.Text.RegularExpressions;
using MhModManager.Models;

namespace MhModManager.Core;

public static class NexusNames
{
    // Nexus 站内下载的压缩包名：名称 id 版本 日期 密钥，如
    // "Dreamspell Magic Staff 4874 4 2026-09-16T11-43Z 8nis61VXS.zip"。
    private static readonly Regex DownloadDate = new(@"^\d{4}-\d{2}-\d{2}T\d{2}-\d{2}Z$", RegexOptions.Compiled);

    // 旧式下载名的版本段：纯数字、可带 v 前缀、允许 "1.5" 点分单段。
    private static readonly Regex VersionToken = new(@"^v?\d+([._]\d+)*$", RegexOptions.Compiled);

    // 旧式下载名末尾的时间戳（秒/毫秒级）。
    private static readonly Regex EpochToken = new(@"^\d{9,13}$", RegexOptions.Compiled);

    public static void Apply(ParsedMod mod, string filePath)
    {
        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return;
        }

        if (stem.StartsWith("cmg_", StringComparison.OrdinalIgnoreCase))
        {
            var parts = stem.Split('_', 3);
            if (parts.Length >= 3)
            {
                mod.HomeUrl = $"https://www.caimogu.cc/post/{parts[1]}.html";
                if (string.IsNullOrWhiteSpace(mod.Name))
                {
                    mod.Name = parts[2].Replace('_', ' ');
                }
            }
        }

        // 下载格式必须先于连字符格式判断，否则日期里的数字段会被旧解析器误当 id。
        var info = ParseNexusDownloadStem(stem) ?? ParseNexusStem(stem);
        if (info is null)
        {
            return;
        }

        if (mod.NexusId <= 0)
        {
            mod.NexusId = info.Value.ModId;
        }

        // Name 等于原始文件名时说明 modinfo 没有提供名称（非 ModuleConfig 路径会用文件名预填）。
        if (string.IsNullOrWhiteSpace(mod.Name) || mod.Name == stem)
        {
            mod.Name = info.Value.Name;
        }

        if (string.IsNullOrWhiteSpace(mod.Version))
        {
            mod.Version = info.Value.Version;
        }
    }

    /// <summary>解析 Nexus 站内下载文件名（空格分隔：名称 id 版本 日期 密钥），不符合返回 null。</summary>
    public static (string Name, int ModId, string Version)? ParseNexusDownloadStem(string stem)
    {
        var tokens = stem.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 5 || !int.TryParse(tokens[^4], out var modId) || modId <= 0)
        {
            return null;
        }

        if (!DownloadDate.IsMatch(tokens[^2]) || !tokens[^1].All(char.IsLetterOrDigit))
        {
            return null;
        }

        var name = string.Join(' ', tokens[..^4]);
        return string.IsNullOrWhiteSpace(name) ? null : (name, modId, tokens[^3]);
    }

    public static (string Name, int ModId, string Version)? ParseNexusStem(string stem)
    {
        var tokens = stem.Split('-');
        if (tokens.Length < 2)
        {
            return null;
        }

        // 旧式下载名以时间戳结尾（名称-id-版本各段-时间戳）：
        // 以末段时间戳为锚从后向前解析，避免名称里的数字段抢占 id、
        // 以及无版本号时时间戳被当成版本。
        var anchored = TryParseTimestampStem(tokens);
        if (anchored is not null)
        {
            return anchored;
        }

        // 无时间戳回退：前向扫描 Name-id-版本…。
        var nameParts = new List<string>();
        var modId = 0;
        var versionParts = new List<string>();

        foreach (var part in tokens)
        {
            if (modId == 0)
            {
                if (int.TryParse(part, out var id) && id > 0)
                {
                    modId = id;
                }
                else
                {
                    nameParts.Add(part);
                }

                continue;
            }

            if (part.Length >= 10 && versionParts.Count > 0)
            {
                break;
            }

            versionParts.Add(StripVersionPrefix(part));
        }

        if (modId == 0)
        {
            return null;
        }

        var forwardName = nameParts.Count > 0 ? string.Join(" ", nameParts) : stem;
        var forwardVersion = versionParts.Count > 0 ? string.Join(".", versionParts) : "";
        return (forwardName, modId, forwardVersion);
    }

    private static (string, int, string)? TryParseTimestampStem(string[] tokens)
    {
        if (!EpochToken.IsMatch(tokens[^1]))
        {
            return null;
        }

        var versionEnd = tokens.Length - 1;
        var versionStart = versionEnd;
        while (versionStart > 0 && VersionToken.IsMatch(tokens[versionStart - 1]))
        {
            versionStart--;
        }

        // 连续段 run = [versionStart, versionEnd)：最左段是 id，其余是版本。
        if (versionStart == 0 || versionStart == versionEnd ||
            !int.TryParse(tokens[versionStart], out var modId) || modId <= 0)
        {
            return null;
        }

        var name = string.Join(' ', tokens[..versionStart]);
        var version = string.Join('.', tokens[(versionStart + 1)..versionEnd].Select(StripVersionPrefix));
        return (name, modId, version);
    }

    private static string StripVersionPrefix(string part) =>
        part.Length > 1 && (part[0] == 'v' || part[0] == 'V') && char.IsDigit(part[1]) ? part[1..] : part;

    public static string GetNexusUrl(GameProfile game, int nexusId) =>
        $"https://www.nexusmods.com/{game.NexusSlug}/mods/{nexusId}";
}
