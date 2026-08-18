namespace HuntForge.Models;

public sealed record GameProfile(
    GameId Id,
    string DisplayName,
    string ShortName,
    string SteamFolder,
    string ExeName,
    int SteamAppId,
    string NexusSlug,
    string PakPrefix,
    int PakBaseId)
{
    public bool UsesPakPatches => !string.IsNullOrEmpty(PakPrefix);

    public bool UsesReFramework => Id != GameId.World;

    public static IReadOnlyList<GameProfile> All { get; } =
    [
        new(GameId.World, "怪物猎人：世界", "世界", "Monster Hunter World",
            "MonsterHunterWorld.exe", 582010, "monsterhunterworld", "", 0),
        new(GameId.Rise, "怪物猎人：崛起", "崛起", "MonsterHunterRise",
            "MonsterHunterRise.exe", 1446780, "monsterhunterrise",
            "re_chunk_000.pak.patch_", 1),
        new(GameId.Wilds, "怪物猎人：荒野", "荒野", "MonsterHunterWilds",
            "MonsterHunterWilds.exe", 2246340, "monsterhunterwilds",
            "re_chunk_000.pak.sub_000.pak.patch_", 6)
    ];

    public static GameProfile Get(GameId id) => All.First(g => g.Id == id);

    public static GameProfile? Find(int steamAppId) =>
        All.FirstOrDefault(g => g.SteamAppId == steamAppId);
}
