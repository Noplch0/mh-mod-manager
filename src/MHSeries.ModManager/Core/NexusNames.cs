using HuntForge.Models;

namespace HuntForge.Core;

public static class NexusNames
{
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

        var info = ParseNexusStem(stem);
        if (info is null)
        {
            return;
        }

        if (mod.NexusId <= 0)
        {
            mod.NexusId = info.Value.ModId;
        }

        if (string.IsNullOrWhiteSpace(mod.Name))
        {
            mod.Name = info.Value.Name;
        }

        if (string.IsNullOrWhiteSpace(mod.Version))
        {
            mod.Version = info.Value.Version;
        }
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
