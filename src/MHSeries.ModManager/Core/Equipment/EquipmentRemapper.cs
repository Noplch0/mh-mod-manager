using MhModManager.Models;

namespace MhModManager.Core.Equipment;

public sealed class EquipmentConflictException(IReadOnlyList<string> paths)
    : InvalidOperationException("目标装备文件已存在或多个文件将写入同一位置，未修改 MOD 文件。")
{
    public IReadOnlyList<string> Paths { get; } = paths;
}

internal static class EquipmentRemapper
{
    private sealed record Edit(string Source, string Destination, string? Replacement, string Backup);

    public static int Apply(GameId game, string filesDir, IEnumerable<string> relativeFiles,
        EquipKind kind, int fromId, int toId, bool isPfb, bool withTex) =>
        Apply(game, filesDir, [("", relativeFiles)], kind, fromId, toId, isPfb, withTex);

    public static int Apply(GameId game, string filesDir, IEnumerable<(string Prefix, IEnumerable<string> Files)> units,
        EquipKind kind, int fromId, int toId, bool isPfb, bool withTex, Action? save = null)
    {
        if (!Directory.Exists(filesDir)) return 0;

        var working = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(filesDir))!, $".remap-{Guid.NewGuid():N}");
        var edits = new List<Edit>();
        var moved = new List<Edit>();
        var installed = new List<Edit>();
        var createdDirs = new List<string>();
        var keepWorking = false;
        var changed = 0;
        try
        {
            foreach (var (prefix, files) in units)
            foreach (var relative in files.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var source = FullPath(prefix + relative);
                if (!File.Exists(source)) continue;
                var backup = Path.Combine(working, $"{edits.Count}.original");
                var replacement = Path.Combine(working, $"{edits.Count}.replacement");
                if (game != GameId.World && ModLayoutParser.IsPakFile(relative))
                {
                    if (!PakReader.CanInspect(source)) continue;
                    Directory.CreateDirectory(working);
                    File.Copy(source, replacement);
                    int rewritten;
                    try
                    {
                        rewritten = PakReader.RewritePaths(replacement, PakFileIndex.For(game), Map,
                            path => Map(path) is { } target ? BuildAvp(target) : null, requireContentPatch: true);
                    }
                    catch (EquipmentConflictException ex)
                    {
                        throw new EquipmentConflictException(ex.Paths.Select(path => $"{prefix}{relative} → {path}").ToList());
                    }

                    if (rewritten == 0)
                    {
                        File.Delete(replacement);
                        continue;
                    }

                    edits.Add(new Edit(source, source, replacement, backup));
                    changed += rewritten;
                }
                else if (Map(relative) is { } target)
                {
                    var payload = BuildAvp(target);
                    if (payload is not null)
                    {
                        Directory.CreateDirectory(working);
                        File.WriteAllBytes(replacement, payload);
                    }

                    edits.Add(new Edit(source, FullPath(prefix + target), payload is null ? null : replacement, backup));
                    changed += payload is null ? 1 : 2;
                }
            }

            var sources = edits.Select(edit => edit.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var collisions = edits.Where(edit => !destinations.Add(edit.Destination)
                    || (File.Exists(edit.Destination) && !sources.Contains(edit.Destination))
                    || Directory.Exists(edit.Destination))
                .Select(edit => Path.GetRelativePath(filesDir, edit.Destination)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (collisions.Count > 0) throw new EquipmentConflictException(collisions);
            if (changed == 0) return 0;

            Directory.CreateDirectory(working);
            foreach (var edit in edits)
            {
                File.Move(edit.Source, edit.Backup);
                moved.Add(edit);
            }

            foreach (var edit in edits)
            {
                EnsureDestination(Path.GetDirectoryName(edit.Destination)!);
                File.Move(edit.Replacement ?? edit.Backup, edit.Destination);
                installed.Add(edit);
            }

            save?.Invoke();
            return changed;
        }
        catch
        {
            try
            {
                // Vacate every destination before restoring sources (a source can also be a destination).
                foreach (var edit in installed.AsEnumerable().Reverse())
                    File.Move(edit.Destination, edit.Replacement ?? edit.Backup);
                foreach (var edit in moved.AsEnumerable().Reverse())
                    File.Move(edit.Backup, edit.Source);
                foreach (var dir in createdDirs.AsEnumerable().Reverse())
                    if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (Exception ex)
            {
                keepWorking = true;
                throw new IOException($"装备替换失败，且无法完整回滚。请保留恢复目录：{working}", ex);
            }

            throw;
        }
        finally
        {
            if (!keepWorking && Directory.Exists(working))
            {
                try { Directory.Delete(working, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        string FullPath(string path) => ModLayoutParser.IsSafeStoredPath(path)
            ? Path.Combine(filesDir, path.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar))
            : throw new InvalidOperationException($"不安全的 MOD 文件路径：{path}");

        void EnsureDestination(string dir)
        {
            if (Directory.Exists(dir)) return;
            EnsureDestination(Path.GetDirectoryName(dir)!);
            Directory.CreateDirectory(dir);
            createdDirs.Add(dir);
        }

        string? Map(string path)
        {
            var normalized = EquipmentResolver.Normalize(path);
            var pfb = EquipmentResolver.TryParse(game, normalized, out _, out _, out var parsedPfb) && parsedPfb;
            var name = Path.GetFileName(normalized);
            var tex = name.Contains(".tex.", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tex", StringComparison.OrdinalIgnoreCase);
            // Armor model swaps also carry their AVP; an AVP shown as a prefab can be swapped on its own.
            var armorAvp = game == GameId.Wilds && EquipKindInfo.IsArmor(kind) && AvpPatcher.IsAvpFile(path);
            if ((pfb != isPfb && !armorAvp) || (!withTex && tex)) return null;
            return EquipmentPathRewriter.TryRewrite(game, kind, fromId, toId, path, out var next)
                && !next.Equals(path, StringComparison.OrdinalIgnoreCase) ? next : null;
        }

        byte[]? BuildAvp(string path) => game == GameId.Wilds && EquipKindInfo.IsArmor(kind) && AvpPatcher.IsAvpFile(path)
            ? AvpPatcher.Build(game, kind, toId) ?? throw new InvalidOperationException("缺少防具 AVP 模板，未修改 MOD 文件。")
            : null;
    }
}
