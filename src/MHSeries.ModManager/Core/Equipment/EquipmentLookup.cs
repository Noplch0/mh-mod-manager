using MhModManager.Models;

namespace MhModManager.Core.Equipment;

public sealed class EquipmentOption
{
    public string Kind { get; init; } = "";
    public int Id { get; init; }
    public string Type { get; init; } = "";
    public string Name { get; init; } = "";
}

public static class EquipmentLookup
{
    public static bool TryParseKind(string? value, out string kind, out string type)
    {
        kind = "";
        type = "";
        if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse<EquipKind>(value, true, out var parsed)
            || !Enum.IsDefined(typeof(EquipKind), parsed))
        {
            return false;
        }

        kind = parsed.ToString();
        type = EquipKindInfo.DisplayName(parsed);
        return true;
    }

    public static IReadOnlyList<EquipmentOption> List(GameId game, string kindName)
    {
        if (!Enum.TryParse<EquipKind>(kindName, true, out var kind) || !Enum.IsDefined(typeof(EquipKind), kind))
        {
            return [];
        }

        return EquipmentCatalog.For(game).List(kind)
            .Select(item => new EquipmentOption
            {
                Kind = kind.ToString(),
                Id = item.Id,
                Type = EquipKindInfo.DisplayName(kind),
                Name = item.Name
            })
            .ToList();
    }

    public static string? DisplayName(string kindName, int id, GameId game)
    {
        if (!Enum.TryParse<EquipKind>(kindName, true, out var kind) || !Enum.IsDefined(typeof(EquipKind), kind))
        {
            return null;
        }

        return EquipmentCatalog.For(game).GetNameOrFallback(kind, id);
    }
}
