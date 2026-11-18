using MhModManager.Core.Equipment;
using MhModManager.Models;

namespace MhModManager.Core;

public sealed record DeploymentConflict(int WinnerId, string Winner, int OverwrittenId, string Overwritten);

internal static class DeploymentConflictDetector
{
    private readonly record struct Target(string? Path, ulong Hash);

    public static IReadOnlyList<DeploymentConflict> Find(GameProfile game, IEnumerable<ModRecord> mods, IReadOnlyList<ModGroup> groups)
    {
        var ordered = GroupOrder.Ordered(mods.Where(mod => mod.Enabled), groups).Reverse().ToList();
        if (ordered.Count < 2) return [];

        var activeOwners = new Dictionary<Target, ModRecord>();
        var active = new HashSet<(ModRecord Winner, ModRecord Loser)>();
        foreach (var mod in ordered)
        {
            foreach (var target in Targets(game, mod))
            {
                if (activeOwners.TryAdd(target, mod)) continue;
                var winner = activeOwners[target];
                if (winner != mod) active.Add((winner, mod));
            }
        }

        return active.Select(pair => new DeploymentConflict(pair.Winner.Id, Name(pair.Winner), pair.Loser.Id, Name(pair.Loser))).ToList();
    }

    private static IEnumerable<Target> Targets(GameProfile game, ModRecord mod)
    {
        foreach (var stored in ModService.EnabledStoredFiles(mod))
        {
            if (!ModLayoutParser.IsSafeStoredPath(stored)) continue;
            var target = mod.DeployPath(stored).Replace('\\', '/');
            if (ModLayoutParser.IsRootJunk(target)) continue;
            if (game.UsesPakMods && ModLayoutParser.IsPakFile(mod.ContentRelative(stored)))
            {
                var path = Path.Combine(AppPaths.ModFilesDir(game.SteamAppId, mod.Id), stored);
                var hashes = ReadPakHashes(path);
                foreach (var hash in hashes) yield return new Target(null, hash);
            }
            else if (ModLayoutParser.IsSafeDeploymentPath(game, target))
            {
                yield return new Target(target.ToLowerInvariant(), 0);
            }
        }
    }

    private static ulong[] ReadPakHashes(string path)
    {
        try { return PakReader.ListHashes(path); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    private static string Name(ModRecord mod) => string.IsNullOrWhiteSpace(mod.DisplayName) ? mod.Name : mod.DisplayName;
}
