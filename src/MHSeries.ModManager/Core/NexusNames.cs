using System.Text.RegularExpressions;
using MhModManager.Models;

namespace MhModManager.Core;

public static class NexusNames
{
    // Nexus 站内下载的压缩包名：名称 id 版本 日期 密钥，如
    // "Dreamspell Magic Staff 4874 4 2026-09-16T11-43Z 8nis61VXS.zip"。
    private static readonly Regex DownloadDate = new(@"^\d{4}-\d{2}-\d{2}T\d{2}-\d{2}Z$", RegexOptions.Compiled);

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
        var parts = stem.Split('-');
        if (parts.Length < 2)
        {
            return null;
        }

        var nameParts = new List<string>();
        var modId = 0;
        var versionParts = new List<string>();

        foreach (var part in parts)
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

            versionParts.Add(part.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? part[1..] : part);
        }

        if (modId == 0)
        {
            return null;
        }

        var name = nameParts.Count > 0 ? string.Join(" ", nameParts) : stem;
        var version = versionParts.Count > 0 ? string.Join(".", versionParts) : "";
        return (name, modId, version);
    }

    public static string GetNexusUrl(GameProfile game, int nexusId) =>
        $"https://www.nexusmods.com/{game.NexusSlug}/mods/{nexusId}";
}
