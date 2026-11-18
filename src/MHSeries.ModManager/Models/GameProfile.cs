namespace MhModManager.Models;

public sealed record GameProfile(
    GameId Id,
    string DisplayName,
    string ShortName,
    string SteamFolder,
    string ExeName,
    int SteamAppId,
    string NexusSlug,
    bool UsesPakMods)
{
    public bool UsesReFramework => Id != GameId.World;

    public static IReadOnlyList<GameProfile> All { get; } =
    [
        new(GameId.World, "怪物猎人：世界", "世界", "Monster Hunter World",
            "MonsterHunterWorld.exe", 582010, "monsterhunterworld", false),
        new(GameId.Rise, "怪物猎人：崛起", "崛起", "MonsterHunterRise",
            "MonsterHunterRise.exe", 1446780, "monsterhunterrise", true),
        new(GameId.Wilds, "怪物猎人：荒野", "荒野", "MonsterHunterWilds",
            "MonsterHunterWilds.exe", 2246340, "monsterhunterwilds", true)
    ];

    public static GameProfile Get(GameId id) => All.First(g => g.Id == id);

    public static GameProfile? Find(int steamAppId) =>
        All.FirstOrDefault(g => g.SteamAppId == steamAppId);
}
