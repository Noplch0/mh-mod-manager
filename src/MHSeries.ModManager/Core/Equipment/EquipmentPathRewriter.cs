using MhModManager.Models;

namespace MhModManager.Core.Equipment;

/// <summary>
/// 把装备路径里的 ID 片段替换成目标套装，对齐 sample.exe 的 GetFilePathTemp / MakeFilePathByTemp。
/// Keys 同时覆盖文件夹和文件名（例如 World 的 pl084_0000 与 f_body084_0000）。
/// </summary>
internal static class EquipmentPathRewriter
{
    public static bool TryRewrite(
        GameId game,
        EquipKind kind,
        int fromId,
        int toId,
        string path,
        out string rewritten)
    {
        rewritten = path;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var lower = path.Replace('\\', '/').ToLowerInvariant();
        var normalized = EquipmentResolver.Normalize(lower);
        string next;
        if (game == GameId.World && EquipKindInfo.IsArmor(kind)
            && EquipmentResolver.TryReadWorldArmorIds(normalized, out var armorKind, out var folderId, out var fileId, out var hasFileId)
            && armorKind == kind)
        {
            var ids = new HashSet<int> { folderId };
            if (hasFileId)
            {
                ids.Add(fileId);
            }

            if (!ids.Contains(fromId))
            {
                return false;
            }

            next = lower;
            foreach (var id in ids.Where(item => item != toId))
            {
                next = Rewrite(game, kind, id, toId, next);
            }
        }
        else
        {
            if (fromId == toId
                || !EquipmentResolver.TryParse(game, normalized, out var parsedKind, out var parsedId, out _)
                || parsedKind != kind || parsedId != fromId)
            {
                return false;
            }

            next = Rewrite(game, kind, fromId, toId, lower);
        }

        if (string.Equals(next, lower, StringComparison.Ordinal)
            || !EquipmentResolver.TryParse(game, EquipmentResolver.Normalize(next), out var newKind, out var newId, out _)
            || newKind != kind || newId != toId)
        {
            return false;
        }

        rewritten = RestoreSeparators(path, next);
        return true;
    }

    private static string Rewrite(GameId game, EquipKind kind, int fromId, int toId, string path)
    {
        var next = path;
        foreach (var (from, to) in Tokens(game, kind, fromId, toId)
                     .Where(token => token.From.Length > 0 && !string.Equals(token.From, token.To, StringComparison.Ordinal))
                     .DistinctBy(token => token.From, StringComparer.Ordinal)
                     .OrderByDescending(token => token.From.Length))
        {
            next = next.Replace(from, to, StringComparison.Ordinal);
        }

        return next;
    }

    private static IEnumerable<(string From, string To)> Tokens(GameId game, EquipKind kind, int fromId, int toId) =>
        game switch
        {
            GameId.World => WorldTokens(kind, fromId, toId),
            GameId.Rise => RiseTokens(kind, fromId, toId),
            _ => WildsTokens(kind, fromId, toId)
        };

    private static IEnumerable<(string From, string To)> WorldTokens(EquipKind kind, int fromId, int toId)
    {
        if (EquipKindInfo.IsArmor(kind))
        {
            SplitWorldArmor(fromId, out var fromFirst, out var fromSecond);
            SplitWorldArmor(toId, out var toFirst, out var toSecond);
            var fromFull = $"{fromFirst:000}_{fromSecond:0000}";
            var toFull = $"{toFirst:000}_{toSecond:0000}";
            yield return ($"pl{fromFull}", $"pl{toFull}");
            yield return (fromFull, toFull);
            foreach (var gender in new[] { "f", "m" })
            {
                foreach (var part in EquipKindInfo.WorldArmorFileParts)
                {
                    yield return ($"{gender}_{part}{fromFull}", $"{gender}_{part}{toFull}");
                    yield return ($"{gender}_{part}{fromFirst:000}", $"{gender}_{part}{toFirst:000}");
                }
            }

            yield break;
        }

        var lite = EquipKindInfo.WorldLiteName(kind);
        yield return (WorldWeaponToken(lite, fromId), WorldWeaponToken(lite, toId));
        var fromPad = (fromId / 10_000).ToString("000");
        var toPad = (toId / 10_000).ToString("000");
        var fromBs = fromId % 10 != 0;
        var toBs = toId % 10 != 0;
        foreach (var extra in new[] { "sld", "saya", "ya", "sou_r" })
        {
            yield return (WeaponCode(fromBs, extra, fromPad), WeaponCode(toBs, extra, toPad));
        }
    }

    private static IEnumerable<(string From, string To)> RiseTokens(EquipKind kind, int fromId, int toId)
    {
        var fromPad = fromId.ToString("000");
        var toPad = toId.ToString("000");
        if (EquipKindInfo.IsArmor(kind))
        {
            yield return ($"pl{fromPad}", $"pl{toPad}");
            foreach (var gender in new[] { "f", "m" })
            {
                foreach (var part in EquipKindInfo.WorldArmorFileParts)
                {
                    yield return ($"{gender}_{part}{fromPad}", $"{gender}_{part}{toPad}");
                }
            }

            yield return ($"{fromPad}.", $"{toPad}.");
            yield break;
        }

        foreach (var folder in EquipKindInfo.RiseWeaponFolders(kind))
        {
            var lower = folder.ToLowerInvariant();
            yield return (lower + fromPad, lower + toPad);
        }

        foreach (var extra in RiseWeaponExtras(kind))
        {
            yield return (extra + fromPad, extra + toPad);
        }
    }

    private static IEnumerable<(string From, string To)> WildsTokens(EquipKind kind, int fromId, int toId)
    {
        SplitWilds(kind, fromId, out var fromFirst, out var fromSecond);
        SplitWilds(kind, toId, out var toFirst, out var toSecond);
        var fromA = FormatWilds(kind, fromFirst, fromSecond);
        var toA = FormatWilds(kind, toFirst, toSecond);
        foreach (var (from, to) in Zip(WildsFragments(kind, fromA.first, fromA.second), WildsFragments(kind, toA.first, toA.second)))
        {
            yield return (from, to);
        }
    }

    private static IEnumerable<string> WildsFragments(EquipKind kind, string first, string second)
    {
        if (kind == EquipKind.PalicoArmor)
        {
            yield return $"/ch05/{first}/{second}";
            yield return $"ch05_{first}_{second}";
            yield return $"{first}_{second}";
            yield break;
        }

        if (EquipKindInfo.IsArmor(kind))
        {
            var tag = kind == EquipKind.FemaleArmor ? "ch03" : "ch02";
            var gender = kind == EquipKind.FemaleArmor ? "female" : "male";
            yield return $"/{tag}/{first}/{second}";
            yield return $"{tag}_{first}_{second}";
            yield return $"/{gender}/{first}/{second}";
            yield return $"{first}_{second}";
            yield break;
        }

        var item = EquipKindInfo.WildsItemFolder(kind);
        var prefab = EquipKindInfo.WildsPrefabFolder(kind);
        yield return $"/{item}/{first}/{second}";
        yield return $"/{prefab}/{first}/{second}";
        yield return $"{item}_{first}_{second}";
        yield return $"{prefab}_{first}_{second}";
        yield return $"{item}{first}_{second}";
        yield return $"{prefab}{first}_{second}";
        yield return $"{first}_{second}";
    }

    private static IEnumerable<string> RiseWeaponExtras(EquipKind kind) => kind switch
    {
        EquipKind.ShortSword => ["ss_sld", "ss_swd"],
        EquipKind.TwinSword => ["db_l", "db_r"],
        EquipKind.Tachi => ["ls_saya", "ls_swd"],
        EquipKind.Lance => ["l_lan", "l_sld"],
        EquipKind.GunLance => ["gl_lan", "gl_sld"],
        EquipKind.ChargeAxe => ["ca_sld", "ca_swd"],
        EquipKind.Bow => ["b_bow", "b_ydt"],
        _ => []
    };

    private static void SplitWorldArmor(int id, out int first, out int second)
    {
        first = id / 10_000;
        second = id % 10_000;
    }

    private static void SplitWilds(EquipKind kind, int id, out int first, out int second)
    {
        if (kind == EquipKind.PalicoArmor || EquipKindInfo.IsWeapon(kind))
        {
            first = id / 10_000;
            second = id % 10_000;
            return;
        }

        first = id / 1_000;
        second = id % 1_000;
    }

    private static (string first, string second) FormatWilds(EquipKind kind, int first, int second)
    {
        if (kind == EquipKind.PalicoArmor)
        {
            return (first.ToString("000"), second.ToString("0000"));
        }

        if (EquipKindInfo.IsWeapon(kind))
        {
            return (first.ToString("00"), second.ToString("0000"));
        }

        return (first.ToString("000"), second.ToString("000"));
    }

    private static string WorldWeaponToken(string lite, int id)
    {
        var first = id / 10_000;
        var isBs = id % 10 != 0;
        return WeaponCode(isBs, lite, first.ToString("000"));
    }

    private static string WeaponCode(bool isBs, string name, string pad) =>
        isBs ? $"bs_{name}{pad}" : $"{name}{pad}";

    private static IEnumerable<(string From, string To)> Zip(IEnumerable<string> from, IEnumerable<string> to) =>
        from.Zip(to, (left, right) => (left, right));

    private static string RestoreSeparators(string original, string rewritten)
    {
        if (original.Contains('\\') && !original.Contains('/'))
        {
            return rewritten.Replace('/', '\\');
        }

        return rewritten;
    }
}
