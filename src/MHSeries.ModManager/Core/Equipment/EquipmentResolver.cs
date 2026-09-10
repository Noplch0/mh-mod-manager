using MhModManager.Models;

namespace MhModManager.Core.Equipment;

public sealed class EquipmentMatch
{
    public string Kind { get; init; } = "";
    public int Id { get; init; }
    public string Type { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsPfb { get; init; }
    public bool IsPak { get; init; }
    public int FileCount { get; init; }

    public string Display
    {
        get
        {
            var text = $"{Type} · {Name}";
            if (IsPfb) text += " [pfb]";
            if (IsPak) text += " [pak]";
            return text;
        }
    }
}

public static class EquipmentResolver
{
    public static IReadOnlyList<EquipmentMatch> Resolve(GameProfile game, IEnumerable<string> files) =>
        Resolve(game.Id, files, null);

    public static IReadOnlyList<EquipmentMatch> Resolve(GameId game, IEnumerable<string> files) =>
        Resolve(game, files, null);

    /// <summary>
    /// pakRootDir 非空时，MOD 文件里的 .pak 补丁会通过游戏文件清单展开内部路径再解析。
    /// </summary>
    public static IReadOnlyList<EquipmentMatch> Resolve(GameId game, IEnumerable<string> files, string? pakRootDir)
    {
        var catalog = EquipmentCatalog.For(game);
        var hits = new Dictionary<string, Hit>(StringComparer.Ordinal);
        var worldOps = new Dictionary<EquipKind, HashSet<int>>();

        foreach (var raw in files)
        {
            var path = Normalize(raw);
            if (path.Length == 0 || IsTex(path))
            {
                continue;
            }

            if (game == GameId.World)
            {
                CollectWorldOps(path, worldOps);
            }

            if (TryParse(game, path, out var kind, out var id, out var isPfb))
            {
                Add(hits, catalog, kind, id, isPfb, isPak: false);
                continue;
            }

            // PAK 补丁：展开内部路径再逐个解析（Rise / Wilds）
            if (pakRootDir is not null && game != GameId.World && IsPakPatch(path))
            {
                ResolvePak(game, pakRootDir, raw, catalog, hits, worldOps);
            }
        }

        if (game == GameId.World)
        {
            ExpandWorldLayeredWeapons(hits, catalog, worldOps);
        }

        return hits.Values
            .OrderByDescending(hit => hit.FileCount)
            .ThenBy(hit => hit.Kind)
            .ThenBy(hit => hit.Name, StringComparer.Ordinal)
            .Select(hit => new EquipmentMatch
            {
                Kind = hit.Kind.ToString(),
                Id = hit.Id,
                Type = EquipKindInfo.DisplayName(hit.Kind),
                Name = hit.Name,
                IsPfb = hit.IsPfb,
                IsPak = hit.IsPak,
                FileCount = hit.FileCount
            })
            .ToList();
    }

    private static void ResolvePak(
        GameId game,
        string pakRootDir,
        string rawFile,
        EquipmentCatalog catalog,
        Dictionary<string, Hit> hits,
        Dictionary<EquipKind, HashSet<int>> worldOps)
    {
        var fullPath = Path.Combine(pakRootDir, rawFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return;
        }

        var internalPaths = PakReader.ListInternalPaths(fullPath, PakFileIndex.For(game));
        foreach (var internalPath in internalPaths)
        {
            var path = Normalize(internalPath);
            if (path.Length == 0 || IsTex(path))
            {
                continue;
            }

            if (game == GameId.World)
            {
                CollectWorldOps(path, worldOps);
            }

            if (TryParse(game, path, out var kind, out var id, out var isPfb))
            {
                Add(hits, catalog, kind, id, isPfb, isPak: true);
            }
        }
    }

    private static bool IsPakPatch(string path) =>
        path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && !path.Contains('/');

    private static void Add(
        Dictionary<string, Hit> hits,
        EquipmentCatalog catalog,
        EquipKind kind,
        int id,
        bool isPfb,
        bool isPak)
    {
        var key = $"{(int)kind}|{id}|{(isPfb ? 1 : 0)}";
        if (hits.TryGetValue(key, out var existing))
        {
            existing.FileCount++;
            existing.IsPak |= isPak;
            return;
        }

        hits[key] = new Hit
        {
            Kind = kind,
            Id = id,
            Name = catalog.GetNameOrFallback(kind, id),
            IsPfb = isPfb,
            IsPak = isPak,
            FileCount = 1
        };
    }

    private static void ExpandWorldLayeredWeapons(
        Dictionary<string, Hit> hits,
        EquipmentCatalog catalog,
        Dictionary<EquipKind, HashSet<int>> worldOps)
    {
        foreach (var hit in hits.Values.ToList())
        {
            if (!EquipKindInfo.IsWeapon(hit.Kind) || hit.Id % 10 == 0
                || !worldOps.TryGetValue(hit.Kind, out var ops))
            {
                continue;
            }

            var first = hit.Id / 10_000;
            foreach (var op in ops)
            {
                Add(hits, catalog, hit.Kind, first * 10_000 + op * 10 + 1, isPfb: false, isPak: hit.IsPak);
            }
        }
    }

    private static void CollectWorldOps(string path, Dictionary<EquipKind, HashSet<int>> worldOps)
    {
        if (!path.StartsWith("nativepc/wp/", StringComparison.Ordinal) || !path.Contains("/parts/op", StringComparison.Ordinal))
        {
            return;
        }

        var folder = NextName(path, "nativepc/wp/");
        if (EquipKindInfo.Parse(GameId.World, folder) is not { } kind || !EquipKindInfo.IsWeapon(kind))
        {
            return;
        }

        var stem = Path.GetFileNameWithoutExtension(path);
        var prefix = "op_" + EquipKindInfo.WorldLiteName(kind);
        if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || stem.Length < 3
            || !int.TryParse(stem[^3..], out var op))
        {
            return;
        }

        if (!worldOps.TryGetValue(kind, out var set))
        {
            set = [];
            worldOps[kind] = set;
        }

        set.Add(op);
    }

    internal static bool TryParse(GameId game, string path, out EquipKind kind, out int id, out bool isPfb)
    {
        kind = default;
        id = 0;
        isPfb = false;
        return game switch
        {
            GameId.World => TryParseWorld(path, out kind, out id),
            GameId.Rise => TryParseRise(path, out kind, out id, out isPfb),
            _ => TryParseWilds(path, out kind, out id, out isPfb)
        };
    }

    private static bool TryParseWorld(string path, out EquipKind kind, out int id)
    {
        kind = default;
        id = 0;
        if (path.StartsWith("nativepc/pl/f_equip/pl", StringComparison.Ordinal)
            || path.StartsWith("nativepc/pl/m_equip/pl", StringComparison.Ordinal))
        {
            return TryParseWorldArmor(path, out kind, out id);
        }

        return path.StartsWith("nativepc/wp/", StringComparison.Ordinal)
               && TryParseWorldWeapon(path, out kind, out id);
    }

    internal static bool TryReadWorldArmorIds(string path, out EquipKind kind, out int folderId, out int fileId, out bool hasFileId)
    {
        kind = default;
        folderId = 0;
        fileId = 0;
        hasFileId = false;
        if (!path.Contains("/mod/", StringComparison.Ordinal))
        {
            return false;
        }

        var gender = NextName(path, "nativepc/pl/");
        kind = gender switch
        {
            "f_equip" => EquipKind.FemaleArmor,
            "m_equip" => EquipKind.MaleArmor,
            _ => default
        };
        if (kind == default)
        {
            return false;
        }

        var folder = NextName(path, "nativepc/pl/" + gender);
        if (!folder.StartsWith("pl", StringComparison.Ordinal) || folder.Length < 3)
        {
            return false;
        }

        var parts = folder[2..].Split('_');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var first) || !int.TryParse(parts[1], out var second))
        {
            return false;
        }

        folderId = first * 10_000 + second;
        var slash = path.LastIndexOf('/');
        var fileName = slash >= 0 ? path[(slash + 1)..] : path;
        hasFileId = TryParseWorldArmorFileName(fileName, out fileId);
        return true;
    }

    internal static bool TryParseWorldArmorFileName(string fileName, out int id)
    {
        id = 0;
        if (fileName.Length < 8 || fileName[1] != '_' || (fileName[0] != 'f' && fileName[0] != 'm'))
        {
            return false;
        }

        var rest = fileName[2..];
        foreach (var part in EquipKindInfo.WorldArmorFileParts.OrderByDescending(item => item.Length))
        {
            if (!rest.StartsWith(part, StringComparison.Ordinal))
            {
                continue;
            }

            var nums = rest[part.Length..];
            if (nums.Length < 8 || nums[3] != '_'
                || !int.TryParse(nums.AsSpan(0, 3), out var first)
                || !int.TryParse(nums.AsSpan(4, 4), out var second))
            {
                return false;
            }

            if (nums.Length > 8 && nums[8] is not ('.' or '_'))
            {
                return false;
            }

            id = first * 10_000 + second;
            return true;
        }

        return false;
    }

    private static bool TryParseWorldArmor(string path, out EquipKind kind, out int id)
    {
        kind = default;
        id = 0;
        if (!TryReadWorldArmorIds(path, out kind, out var folderId, out var fileId, out var hasFileId))
        {
            return false;
        }

        id = hasFileId ? fileId : folderId;
        return true;
    }

    private static bool TryParseWorldWeapon(string path, out EquipKind kind, out int id)
    {
        kind = default;
        id = 0;
        var folder = NextName(path, "nativepc/wp/");
        if (EquipKindInfo.Parse(GameId.World, folder) is not { } parsed || !EquipKindInfo.IsWeapon(parsed)
            || !path.Contains("/mod/", StringComparison.Ordinal))
        {
            return false;
        }

        kind = parsed;
        var code = NextName(path, "nativepc/wp/" + folder);
        if (code.Length < 6 || !int.TryParse(code[^3..], out var first))
        {
            return false;
        }

        var isBs = code.StartsWith("bs_", StringComparison.OrdinalIgnoreCase);
        id = first * 10_000 + (isBs ? 1 : 0);
        return true;
    }

    private static bool TryParseRise(string path, out EquipKind kind, out int id, out bool isPfb)
    {
        kind = default;
        id = 0;
        isPfb = false;
        if (StartsWith(path, "natives/player/prefab/weapon") && IsPfb(path))
        {
            isPfb = true;
            return TryParseRiseWeaponPrefab(path, out kind, out id);
        }

        if (StartsWith(path, "natives/player/prefab/mod") && IsPfb(path))
        {
            isPfb = true;
            return TryParseRiseArmor(path, "natives/player/prefab/mod", requireIdInFileName: false, out kind, out id);
        }

        if (StartsWith(path, "natives/player/mod"))
        {
            return TryParseRiseArmor(path, "natives/player/mod", requireIdInFileName: true, out kind, out id);
        }

        return StartsWith(path, "natives/weapon") && TryParseRiseWeapon(path, out kind, out id);
    }

    private static bool TryParseRiseArmor(string path, string prefix, bool requireIdInFileName, out EquipKind kind, out int id)
    {
        kind = default;
        id = 0;
        var gender = NextName(path, prefix);
        kind = gender switch
        {
            "f" => EquipKind.FemaleArmor,
            "m" => EquipKind.MaleArmor,
            _ => default
        };
        if (kind == default)
        {
            return false;
        }

        var folder = NextName(path, Combine(prefix, gender));
        if (!folder.StartsWith("pl", StringComparison.Ordinal) || !int.TryParse(folder[2..], out id))
        {
            return false;
        }

        if (!requireIdInFileName)
        {
            return true;
        }

        var fileName = Path.GetFileName(path);
        return fileName.Contains(id.ToString("000"), StringComparison.Ordinal);
    }

    private static bool TryParseRiseWeapon(string path, out EquipKind kind, out int id)
    {
        kind = default;
        id = 0;
        var folder = NextName(path, "natives/weapon");
        if (EquipKindInfo.Parse(GameId.Rise, folder) is not { } parsed || !EquipKindInfo.IsWeapon(parsed))
        {
            return false;
        }

        kind = parsed;
        var code = NextName(path, Combine("natives/weapon", folder));
        return code.Length >= 3 && int.TryParse(code[^3..], out id);
    }

    private static bool TryParseRiseWeaponPrefab(string path, out EquipKind kind, out int id)
    {
        kind = default;
        id = 0;
        var folder = NextName(path, "natives/player/prefab/weapon");
        if (EquipKindInfo.Parse(GameId.Rise, folder) is not { } parsed || !EquipKindInfo.IsWeapon(parsed))
        {
            return false;
        }

        kind = parsed;
        var fileName = Path.GetFileName(path);
        var pfb = fileName.IndexOf(".pfb", StringComparison.OrdinalIgnoreCase);
        return pfb >= 3 && int.TryParse(fileName.AsSpan(pfb - 3, 3), out id);
    }

    private static bool TryParseWilds(string path, out EquipKind kind, out int id, out bool isPfb)
    {
        kind = default;
        id = 0;
        isPfb = false;
        if (StartsWith(path, "natives/gamedesign/equip/_prefab/weapon"))
        {
            isPfb = true;
            return TryParseWildsWeapon(path, "natives/gamedesign/equip/_prefab/weapon", fromPrefab: true, out kind, out id);
        }

        if (StartsWith(path, "natives/gamedesign/equip/_prefab/armor"))
        {
            isPfb = true;
            return TryParseWildsArmorPrefab(path, out kind, out id);
        }

        if (StartsWith(path, "natives/art/model/character"))
        {
            return TryParseWildsArmor(path, out kind, out id);
        }

        return StartsWith(path, "natives/art/model/item")
               && TryParseWildsWeapon(path, "natives/art/model/item", fromPrefab: false, out kind, out id);
    }

    private static bool TryParseWildsArmor(string path, out EquipKind kind, out int id)
    {
        kind = default;
        id = 0;
        var tag = NextName(path, "natives/art/model/character");
        kind = tag switch
        {
            "ch03" => EquipKind.FemaleArmor,
            "ch02" => EquipKind.MaleArmor,
            "ch05" => EquipKind.PalicoArmor,
            _ => default
        };
        if (kind == default)
        {
            return false;
        }

        var firstText = NextName(path, Combine("natives/art/model/character", tag));
        var secondText = NextName(path, Combine("natives/art/model/character", tag, firstText));
        if (!int.TryParse(firstText, out var first) || !int.TryParse(secondText, out var second))
        {
            return false;
        }

        id = kind == EquipKind.PalicoArmor ? first * 10_000 + second : first * 1_000 + second;
        return true;
    }

    private static bool TryParseWildsArmorPrefab(string path, out EquipKind kind, out int id)
    {
        kind = default;
        id = 0;
        var gender = NextName(path, "natives/gamedesign/equip/_prefab/armor");
        kind = gender switch
        {
            "female" => EquipKind.FemaleArmor,
            "male" => EquipKind.MaleArmor,
            _ => default
        };
        if (kind == default)
        {
            return false;
        }

        var firstText = NextName(path, Combine("natives/gamedesign/equip/_prefab/armor", gender));
        var secondText = NextName(path, Combine("natives/gamedesign/equip/_prefab/armor", gender, firstText));
        return int.TryParse(firstText, out var first)
               && int.TryParse(secondText, out var second)
               && (id = first * 1_000 + second) >= 0;
    }

    private static bool TryParseWildsWeapon(string path, string prefix, bool fromPrefab, out EquipKind kind, out int id)
    {
        kind = default;
        id = 0;
        var folder = NextName(path, prefix);
        var parsed = fromPrefab
            ? EquipKindInfo.FromWildsPrefabFolder(folder)
            : EquipKindInfo.FromWildsItemFolder(folder);
        if (parsed is null)
        {
            return false;
        }

        kind = parsed.Value;
        var firstText = NextName(path, Combine(prefix, folder));
        var secondText = NextName(path, Combine(prefix, folder, firstText));
        return int.TryParse(firstText, out var first)
               && int.TryParse(secondText, out var second)
               && (id = first * 10_000 + second) >= 0;
    }

    internal static string Normalize(string path)
    {
        var value = path.Replace('\\', '/').Trim('/').ToLowerInvariant();
        const string stm = "natives/stm/";
        return value.StartsWith(stm, StringComparison.Ordinal)
            ? "natives/" + value[stm.Length..]
            : value;
    }

    private static bool StartsWith(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.Ordinal);

    private static bool IsTex(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains(".tex.", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".tex", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPfb(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains(".pfb.", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".pfb", StringComparison.OrdinalIgnoreCase);
    }

    internal static string NextName(string path, string prefix)
    {
        if (path.Length < prefix.Length)
        {
            return "";
        }

        var rest = path[prefix.Length..];
        var index = rest.IndexOf('/', 1);
        if (index <= 0)
        {
            return rest.Trim('/');
        }

        return rest.StartsWith('/')
            ? rest.Substring(1, index - 1)
            : rest[..index];
    }

    private static string Combine(params string[] parts) =>
        string.Join('/', parts.Where(part => !string.IsNullOrEmpty(part))).Replace("//", "/");

    private sealed class Hit
    {
        public EquipKind Kind { get; init; }
        public int Id { get; init; }
        public string Name { get; init; } = "";
        public bool IsPfb { get; init; }
        public bool IsPak { get; set; }
        public int FileCount { get; set; }
    }
}
