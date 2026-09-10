using MhModManager.Models;

namespace MhModManager.Core.Equipment;

internal enum EquipKind
{
    LongSword = 0,
    ShortSword = 1,
    TwinSword = 2,
    Tachi = 3,
    Hammer = 4,
    Whistle = 5,
    Lance = 6,
    GunLance = 7,
    SlashAxe = 8,
    ChargeAxe = 9,
    Rod = 10,
    Bow = 11,
    HeavyBowgun = 12,
    LightBowgun = 13,
    RodInsect = 14,
    MaleArmor = 15,
    FemaleArmor = 16,
    PalicoArmor = 17
}

internal static class EquipKindInfo
{
    private static readonly string[] DisplayNames =
    [
        "大剑", "片手剑", "双剑", "太刀", "大锤", "狩猎笛", "长枪", "铳枪",
        "斩斧", "盾斧", "操虫棍", "弓", "重弩", "轻弩", "猎虫", "男装备", "女装备", "随从猫"
    ];

    private static readonly Dictionary<string, EquipKind> EnumNames = Build(
        ("LONG_SWORD", EquipKind.LongSword), ("SHORT_SWORD", EquipKind.ShortSword),
        ("TWIN_SWORD", EquipKind.TwinSword), ("TACHI", EquipKind.Tachi),
        ("HAMMER", EquipKind.Hammer), ("WHISTLE", EquipKind.Whistle),
        ("LANCE", EquipKind.Lance), ("GUN_LANCE", EquipKind.GunLance),
        ("SLASH_AXE", EquipKind.SlashAxe), ("CHARGE_AXE", EquipKind.ChargeAxe),
        ("ROD", EquipKind.Rod), ("BOW", EquipKind.Bow),
        ("HEAVY_BOWGUN", EquipKind.HeavyBowgun), ("LIGHT_BOWGUN", EquipKind.LightBowgun),
        ("RodInsect", EquipKind.RodInsect), ("ManEquip", EquipKind.MaleArmor),
        ("FemanEquip", EquipKind.FemaleArmor), ("CatEquip", EquipKind.PalicoArmor),
        ("LongSword", EquipKind.LongSword), ("ShortSword", EquipKind.ShortSword),
        ("TwinSword", EquipKind.TwinSword), ("MaleArmor", EquipKind.MaleArmor),
        ("FemaleArmor", EquipKind.FemaleArmor), ("PalicoArmor", EquipKind.PalicoArmor));

    private static readonly Dictionary<string, EquipKind> WorldAliases = Build(
        ("two", EquipKind.LongSword), ("one", EquipKind.ShortSword), ("sou", EquipKind.TwinSword),
        ("swo", EquipKind.Tachi), ("ham", EquipKind.Hammer), ("hue", EquipKind.Whistle),
        ("lan", EquipKind.Lance), ("gun", EquipKind.GunLance), ("saxe", EquipKind.SlashAxe),
        ("caxe", EquipKind.ChargeAxe), ("rod", EquipKind.Rod), ("bow", EquipKind.Bow),
        ("hbg", EquipKind.HeavyBowgun), ("lbg", EquipKind.LightBowgun), ("mus", EquipKind.RodInsect),
        ("m", EquipKind.MaleArmor), ("f", EquipKind.FemaleArmor));

    private static readonly Dictionary<string, EquipKind> RiseAliases = Build(
        ("GreatSword", EquipKind.LongSword), ("ShortSword", EquipKind.ShortSword), ("DualBlades", EquipKind.TwinSword),
        ("LongSword", EquipKind.Tachi), ("Hammer", EquipKind.Hammer), ("Horn", EquipKind.Whistle),
        ("Lance", EquipKind.Lance), ("GunLance", EquipKind.GunLance), ("SlashAxe", EquipKind.SlashAxe),
        ("ChargeAxe", EquipKind.ChargeAxe), ("InsectGlaive", EquipKind.Rod), ("Bow", EquipKind.Bow),
        ("HeavyBowgun", EquipKind.HeavyBowgun), ("LightBowgun", EquipKind.LightBowgun), ("IG_Insect", EquipKind.RodInsect),
        ("FemanEquip", EquipKind.FemaleArmor), ("ManEquip", EquipKind.MaleArmor),
        ("G_Swd", EquipKind.LongSword), ("S_Swd", EquipKind.ShortSword), ("D_Bld", EquipKind.TwinSword),
        ("L_Swd", EquipKind.Tachi), ("Ham", EquipKind.Hammer), ("Hrn", EquipKind.Whistle),
        ("Lan", EquipKind.Lance), ("G_Lan", EquipKind.GunLance), ("S_Axe", EquipKind.SlashAxe),
        ("C_Axe", EquipKind.ChargeAxe), ("I_Gla", EquipKind.Rod), ("BOW", EquipKind.Bow),
        ("H_Bg", EquipKind.HeavyBowgun), ("L_Bg", EquipKind.LightBowgun), ("IG_Ins", EquipKind.RodInsect),
        ("f", EquipKind.FemaleArmor), ("m", EquipKind.MaleArmor));

    private static readonly Dictionary<string, EquipKind> WildsAliases = Build(
        ("LONG_SWORD", EquipKind.LongSword), ("SHORT_SWORD", EquipKind.ShortSword), ("TWIN_SWORD", EquipKind.TwinSword),
        ("TACHI", EquipKind.Tachi), ("HAMMER", EquipKind.Hammer), ("WHISTLE", EquipKind.Whistle),
        ("LANCE", EquipKind.Lance), ("GUN_LANCE", EquipKind.GunLance), ("SLASH_AXE", EquipKind.SlashAxe),
        ("CHARGE_AXE", EquipKind.ChargeAxe), ("ROD", EquipKind.Rod), ("BOW", EquipKind.Bow),
        ("HEAVY_BOWGUN", EquipKind.HeavyBowgun), ("LIGHT_BOWGUN", EquipKind.LightBowgun), ("RodInsect", EquipKind.RodInsect),
        ("FemanEquip", EquipKind.FemaleArmor), ("ManEquip", EquipKind.MaleArmor),
        ("wp00", EquipKind.LongSword), ("wp01", EquipKind.ShortSword), ("wp02", EquipKind.TwinSword),
        ("wp03", EquipKind.Tachi), ("wp04", EquipKind.Hammer), ("wp05", EquipKind.Whistle),
        ("wp06", EquipKind.Lance), ("wp07", EquipKind.GunLance), ("wp08", EquipKind.SlashAxe),
        ("wp09", EquipKind.ChargeAxe), ("wp10", EquipKind.Rod), ("wp11", EquipKind.Bow),
        ("wp12", EquipKind.HeavyBowgun), ("wp13", EquipKind.LightBowgun), ("wp14", EquipKind.RodInsect),
        ("f", EquipKind.FemaleArmor), ("m", EquipKind.MaleArmor));

    public static string DisplayName(EquipKind kind)
    {
        var index = (int)kind;
        return index >= 0 && index < DisplayNames.Length ? DisplayNames[index] : kind.ToString();
    }

    public static bool IsArmor(EquipKind kind) => kind is EquipKind.MaleArmor or EquipKind.FemaleArmor;

    public static readonly string[] WorldArmorFileParts = ["arm", "body", "helm", "leg", "wst", "hand"];

    public static bool IsWeapon(EquipKind kind, bool withInsect = true) =>
        kind is not (EquipKind.MaleArmor or EquipKind.FemaleArmor or EquipKind.PalicoArmor)
        && (kind != EquipKind.RodInsect || withInsect);

    public static string WorldLiteName(EquipKind kind) => kind switch
    {
        EquipKind.LongSword => "two",
        EquipKind.ShortSword => "one",
        EquipKind.TwinSword => "sou",
        EquipKind.Tachi => "swo",
        EquipKind.Hammer => "ham",
        EquipKind.Whistle => "hue",
        EquipKind.Lance => "lan",
        EquipKind.GunLance => "gun",
        EquipKind.SlashAxe => "saxe",
        EquipKind.ChargeAxe => "caxe",
        EquipKind.Rod => "rod",
        EquipKind.Bow => "bow",
        EquipKind.HeavyBowgun => "hbg",
        EquipKind.LightBowgun => "lbg",
        EquipKind.RodInsect => "mus",
        _ => kind.ToString()
    };

    public static string[] RiseWeaponFolders(EquipKind kind) => kind switch
    {
        EquipKind.LongSword => ["G_Swd", "GreatSword"],
        EquipKind.ShortSword => ["S_Swd", "ShortSword"],
        EquipKind.TwinSword => ["D_Bld", "DualBlades"],
        EquipKind.Tachi => ["L_Swd", "LongSword"],
        EquipKind.Hammer => ["Ham", "Hammer"],
        EquipKind.Whistle => ["Hrn", "Horn"],
        EquipKind.Lance => ["Lan", "Lance"],
        EquipKind.GunLance => ["G_Lan", "GunLance"],
        EquipKind.SlashAxe => ["S_Axe", "SlashAxe"],
        EquipKind.ChargeAxe => ["C_Axe", "ChargeAxe"],
        EquipKind.Rod => ["I_Gla", "InsectGlaive"],
        EquipKind.Bow => ["BOW", "Bow"],
        EquipKind.HeavyBowgun => ["H_Bg", "HeavyBowgun"],
        EquipKind.LightBowgun => ["L_Bg", "LightBowgun"],
        EquipKind.RodInsect => ["IG_Ins", "IG_Insect"],
        _ => []
    };

    public static string WildsItemFolder(EquipKind kind) => $"it{(int)kind:00}";

    public static string WildsPrefabFolder(EquipKind kind) => $"wp{(int)kind:00}";

    public static EquipKind? Parse(GameId game, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var aliases = game switch
        {
            GameId.World => WorldAliases,
            GameId.Rise => RiseAliases,
            _ => WildsAliases
        };
        if (aliases.TryGetValue(value, out var kind) || EnumNames.TryGetValue(value, out kind))
        {
            return kind;
        }

        return null;
    }

    public static EquipKind? FromWildsItemFolder(string folder)
    {
        if (!folder.StartsWith("it", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(folder[2..], out var id)
            || !Enum.IsDefined(typeof(EquipKind), id))
        {
            return null;
        }

        var kind = (EquipKind)id;
        return IsWeapon(kind) ? kind : null;
    }

    public static EquipKind? FromWildsPrefabFolder(string folder)
    {
        if (!folder.StartsWith("wp", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(folder[2..], out var id)
            || !Enum.IsDefined(typeof(EquipKind), id))
        {
            return null;
        }

        var kind = (EquipKind)id;
        return IsWeapon(kind) ? kind : null;
    }

    private static Dictionary<string, EquipKind> Build(params (string Alias, EquipKind Kind)[] items)
    {
        var map = new Dictionary<string, EquipKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, kind) in items)
        {
            map[alias] = kind;
        }

        return map;
    }
}
