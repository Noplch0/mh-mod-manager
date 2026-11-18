using MhModManager.Core;
using MhModManager.Core.Equipment;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

public sealed class DeploymentConflictTests
{
    [Fact]
    public void ConflictReportsFinalWinnerAndIgnoresInactiveComponentAndMod()
    {
        var game = GameProfile.Get(GameId.World);
        var first = new ModRecord { Id = 1, Name = "first", GroupId = 10, Enabled = true, Files = ["nativePC/Shared.bin", "nativePC/other.bin"] };
        var second = new ModRecord { Id = 2, Name = "second", GroupId = 20, Enabled = true, Files = ["nativepc/shared.bin"] };
        var winner = new ModRecord { Id = 3, Name = "winner", GroupId = 20, Index = 2, Enabled = true, Components =
        [
            new() { Id = 1, Enabled = true, Files = ["c1/nativePC/shared.bin"] },
            new() { Id = 2, Enabled = false, Files = ["c2/nativePC/other.bin"] }
        ] };
        var disabled = new ModRecord { Id = 4, GroupId = 20, Index = 3, Files = ["nativePC/shared.bin"] };
        var groups = new List<ModGroup> { new() { Id = 20, Index = 2 }, new() { Id = 10, Index = 1 } };

        var conflicts = DeploymentConflictDetector.Find(game, [winner, first, disabled, second], groups);

        Assert.Equal(new[] { 1, 2 }, conflicts.Select(item => item.OverwrittenId).Order());
        Assert.All(conflicts, conflict => Assert.Equal(winner.Id, conflict.WinnerId));

        winner.Enabled = false;
        var remaining = Assert.Single(DeploymentConflictDetector.Find(game, [winner, first, disabled, second], groups));
        Assert.Equal(second.Id, remaining.WinnerId);
        Assert.Equal(first.Id, remaining.OverwrittenId);
    }

    [Fact]
    public void DifferentPakNamesReportAnInternalPathConflict()
    {
        var game = new GameProfile(GameId.Wilds, "conflict-test", "test", "test", "test.exe",
            Random.Shared.Next(9_000_001, 9_900_000), "test", true);
        var first = new ModRecord { Id = 1001, Name = "first", Index = 1, Enabled = true, Files = ["one.pak"] };
        var last = new ModRecord { Id = 1002, Name = "last", Index = 2, Enabled = true, Files = ["two.pak"] };
        const string internalPath = "natives/STM/GameDesign/Equip/_Prefab/Armor/Female/001/001/001_001_avp.user.3";

        try
        {
            foreach (var mod in new[] { first, last })
            {
                var dir = AppPaths.ModFilesDir(game.SteamAppId, mod.Id);
                Directory.CreateDirectory(dir);
                using var writer = new BinaryWriter(File.Create(Path.Combine(dir, Assert.Single(mod.Files))));
                writer.Write(0x414B504Bu);
                writer.Write((byte)4);
                writer.Write((byte)0);
                writer.Write((short)0);
                writer.Write(1);
                writer.Write(0u);
                writer.Write(PakHash.PathHash(internalPath));
                writer.Write(new byte[40]);
            }

            var result = DeploymentConflictDetector.Find(game, [first, last], []);
            var conflict = Assert.Single(result);
            Assert.Equal(last.Id, conflict.WinnerId);
            Assert.Equal(first.Id, conflict.OverwrittenId);
        }
        finally
        {
            Directory.Delete(AppPaths.GameDir(game.SteamAppId), recursive: true);
        }
    }
}
