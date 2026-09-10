using System.Reflection;
using System.Text;
using MhModManager.Models;

namespace MhModManager.Core.Equipment;

internal sealed class EquipmentCatalog
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly Dictionary<GameId, EquipmentCatalog> Cache = [];
    private readonly Dictionary<(EquipKind Kind, int Id), string> _names = [];

    public static EquipmentCatalog For(GameId game)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(game, out var catalog))
            {
                catalog = Load(game);
                Cache[game] = catalog;
            }

            return catalog;
        }
    }

    public string? GetName(EquipKind kind, int id) =>
        _names.TryGetValue((kind, id), out var name) ? name : null;

    public string GetNameOrFallback(EquipKind kind, int id) =>
        GetName(kind, id) ?? $"{EquipKindInfo.DisplayName(kind)}@{id}";

    public IReadOnlyList<(int Id, string Name)> List(EquipKind kind) =>
        _names
            .Where(pair => pair.Key.Kind == kind)
            .Select(pair => (pair.Key.Id, pair.Value))
            .OrderBy(item => item.Id)
            .ToList();

    private void Add(EquipKind kind, int id, string name)
    {
        if (id < 0 || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _names.TryAdd((kind, id), name);
    }

    private static EquipmentCatalog Load(GameId game)
    {
        var catalog = new EquipmentCatalog();
        switch (game)
        {
            case GameId.World:
                catalog.ReadLines("mhw_armor.csv", ParseWorldArmor);
                catalog.ReadLines("mhw_weapon.csv", ParseWorldWeapon);
                break;
            case GameId.Rise:
                catalog.ReadLines("mhr_armor.csv", ParseRiseArmor);
                catalog.ReadLines("mhr_weapon.csv", ParseRiseWeapon);
                break;
            default:
                catalog.ReadLines("mhws_armor.csv", ParseWildsArmor);
                catalog.ReadLines("mhws_weapon.csv", ParseWildsWeapon);
                catalog.ReadLines("mhws_cat_armor.csv", ParseWildsCat);
                break;
        }

        return catalog;
    }

    private void ReadLines(string fileName, Action<EquipmentCatalog, string> parse)
    {
        var text = ReadResource(fileName);
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            try
            {
                parse(this, line);
            }
            catch
            {
                // Ignore malformed rows in the embedded tables.
            }
        }
    }

    private static void ParseWorldArmor(EquipmentCatalog catalog, string line)
    {
        var cols = SplitCsv(line);
        if (cols.Length < 2)
        {
            return;
        }

        var parts = cols[0].Split('_');
        if (parts.Length < 2 || parts[0].Length < 3
            || !int.TryParse(parts[0][2..], out var first)
            || !int.TryParse(parts[1], out var second))
        {
            return;
        }

        var id = first * 10_000 + second;
        var name = cols[1].Trim();
        catalog.Add(EquipKind.MaleArmor, id, name);
        catalog.Add(EquipKind.FemaleArmor, id, name);
    }

    private static void ParseWorldWeapon(EquipmentCatalog catalog, string line)
    {
        var cols = SplitCsv(line);
        if (cols.Length < 3)
        {
            return;
        }

        var declared = EquipKindInfo.Parse(GameId.World, cols[0].Trim());
        var code = cols[1].Trim();
        if (declared is null || code.Length < 3)
        {
            return;
        }

        if (!int.TryParse(code[^3..], out var first))
        {
            return;
        }

        var isBs = code.StartsWith("bs_", StringComparison.OrdinalIgnoreCase);
        var prefix = code[..^3];
        if (isBs)
        {
            prefix = prefix.Length > 3 ? prefix[3..] : prefix;
        }

        var fromCode = EquipKindInfo.Parse(GameId.World, prefix);
        if (fromCode is null || fromCode != declared)
        {
            return;
        }

        var op = 0;
        if (cols.Length > 3 && !string.IsNullOrWhiteSpace(cols[3]) && cols[3].Trim().Length >= 3)
        {
            int.TryParse(cols[3].Trim()[^3..], out op);
        }

        catalog.Add(declared.Value, first * 10_000 + op * 10 + (isBs ? 1 : 0), cols[2].Trim());
    }

    private static void ParseRiseArmor(EquipmentCatalog catalog, string line)
    {
        var cols = SplitCsv(line);
        if (cols.Length < 2)
        {
            return;
        }

        var code = cols[0].Trim();
        var idText = code.StartsWith("pl", StringComparison.OrdinalIgnoreCase) ? code[2..] : code;
        if (!int.TryParse(idText, out var id))
        {
            return;
        }

        var name = cols[1].Trim();
        catalog.Add(EquipKind.FemaleArmor, id, name);
        catalog.Add(EquipKind.MaleArmor, id, name);
    }

    private static void ParseRiseWeapon(EquipmentCatalog catalog, string line)
    {
        var cols = SplitCsv(line);
        if (cols.Length < 3 || EquipKindInfo.Parse(GameId.Rise, cols[0].Trim()) is not { } kind
            || !int.TryParse(cols[1].Trim(), out var id))
        {
            return;
        }

        catalog.Add(kind, id, cols[2].Trim());
    }

    private static void ParseWildsArmor(EquipmentCatalog catalog, string line)
    {
        if (!TryParseWildsIdName(line, out var first, out var second, out var name))
        {
            return;
        }

        var id = first * 1_000 + second;
        catalog.Add(EquipKind.FemaleArmor, id, name);
        catalog.Add(EquipKind.MaleArmor, id, name);
    }

    private static void ParseWildsCat(EquipmentCatalog catalog, string line)
    {
        if (!TryParseWildsIdName(line, out var first, out var second, out var name))
        {
            return;
        }

        catalog.Add(EquipKind.PalicoArmor, first * 10_000 + second, name);
    }

    private static void ParseWildsWeapon(EquipmentCatalog catalog, string line)
    {
        var cols = SplitCsv(line);
        if (cols.Length < 3 || EquipKindInfo.Parse(GameId.Wilds, cols[0].Trim()) is not { } kind)
        {
            return;
        }

        var parts = cols[1].Trim().Split('_');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var first) || !int.TryParse(parts[1], out var second))
        {
            return;
        }

        catalog.Add(kind, first * 10_000 + second, cols[2].Trim());
    }

    private static bool TryParseWildsIdName(string line, out int first, out int second, out string name)
    {
        first = 0;
        second = 0;
        name = "";
        var cols = SplitCsv(line);
        if (cols.Length < 2)
        {
            return false;
        }

        var parts = cols[0].Split('_');
        if (parts.Length == 0 || !int.TryParse(parts[0], out first))
        {
            return false;
        }

        second = parts.Length > 1 && int.TryParse(parts[1], out var parsed) ? parsed : 0;
        name = cols[1].Trim();
        return name.Length > 0;
    }

    private static string[] SplitCsv(string line) => line.Split(',');

    private static string ReadResource(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = $"MhModManager.Data.Equipment.{fileName}";
        using var stream = assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"缺少装备对照表: {fileName}");
        using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
