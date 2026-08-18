using System.Diagnostics;
using HuntForge.Models;

namespace HuntForge.Core;

public sealed class ModService
{
    private readonly SettingsStore _settings;
    private readonly Dictionary<GameId, List<ModRecord>> _cache = [];
    private readonly Dictionary<GameId, List<ModGroup>> _groups = [];

    public ModService(SettingsStore settings)
    {
        _settings = settings;
    }

    public string? ResolveGamePath(GameProfile game)
    {
        var stored = _settings.GetGamePath(game);
        if (SteamLocator.IsGameDir(stored, game.ExeName))
        {
            return stored;
        }

        var found = SteamLocator.FindGamePath(game);
        if (found is not null)
        {
            _settings.SetGamePath(game, found);
        }

        return found;
    }

    public List<ModRecord> GetMods(GameProfile game)
    {
        if (!_cache.TryGetValue(game.Id, out var mods))
        {
            mods = ModRepository.LoadAll(game);
            var groups = ModRepository.LoadGroups(game, mods);
            _groups[game.Id] = groups;
            ModRepository.FixIndex(game, mods, groups);
            _cache[game.Id] = mods;
        }

        return mods;
    }

    public List<ModGroup> GetGroups(GameProfile game)
    {
        GetMods(game);
        return _groups[game.Id];
    }

    public void Invalidate(GameProfile game)
    {
        _cache.Remove(game.Id);
        _groups.Remove(game.Id);
    }

    public ParsedMod ParseImport(GameProfile game, string path, IProgress<int>? progress = null)
    {
        AppPaths.EnsureCreated();
        var staging = Path.Combine(AppPaths.TempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            if (Directory.Exists(path))
            {
                CopyDirectory(path, staging);
            }
            else if (ArchiveExtractor.IsArchive(path))
            {
                ArchiveExtractor.Extract(path, staging, progress);
                ArchiveExtractor.ExtractNested(staging);
            }
            else
            {
                File.Copy(path, Path.Combine(staging, Path.GetFileName(path)), true);
            }

            var parsed = ModLayoutParser.Parse(game, path, staging);
            if (parsed.Files.Count == 0)
            {
                throw new InvalidOperationException("未能识别这个 MOD 的文件结构");
            }

            return parsed;
        }
        catch
        {
            TryDelete(staging);
            throw;
        }
    }

    public ModRecord Install(GameProfile game, ParsedMod parsed)
    {
        ValidateParsedMod(game, parsed);
        var mods = GetMods(game);
        var record = new ModRecord
        {
            Name = parsed.Name,
            DisplayName = string.IsNullOrWhiteSpace(parsed.Name) ? Path.GetFileNameWithoutExtension(parsed.SourceFile) : parsed.Name,
            Version = parsed.Version,
            Author = parsed.Author,
            Category = parsed.Category,
            SourceFile = parsed.SourceFile,
            HomeUrl = parsed.HomeUrl,
            NexusId = parsed.NexusId,
            InstallTime = DateTimeOffset.Now
        };

        if (record.NexusId > 0 && string.IsNullOrWhiteSpace(record.HomeUrl))
        {
            record.HomeUrl = NexusNames.GetNexusUrl(game, record.NexusId);
        }

        ModRepository.MakeId(mods, GetGroups(game), record);
        var destDir = AppPaths.ModFilesDir(game.SteamAppId, record.Id);
        if (Directory.Exists(destDir))
        {
            Directory.Delete(destDir, true);
        }

        try
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in parsed.Files)
            {
                var dest = Path.Combine(destDir, file.RelativeDest.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                CopyVerified(file.SourcePath, dest);
                record.Files.Add(file.RelativeDest);
            }

            if (!string.IsNullOrWhiteSpace(parsed.PreviewSource) && File.Exists(parsed.PreviewSource))
            {
                var preview = Path.Combine(destDir, "screenshot.png");
                File.Copy(parsed.PreviewSource, preview, true);
                record.PreviewImage = preview;
            }

            record.SourceFile = KeepSource(parsed.SourceFile, parsed.Name);
            ModRepository.Save(game, record);
            mods.Add(record);
            return record;
        }
        catch
        {
            TryDelete(AppPaths.ModDir(game.SteamAppId, record.Id));
            throw;
        }
        finally
        {
            TryDelete(parsed.StagingDir);
        }
    }

    public void SetEnabled(GameProfile game, ModRecord mod, bool enable)
    {
        var gamePath = RequireGamePath(game);
        if (_settings.Current.CheckGameRunning && IsGameRunning(game, gamePath))
        {
            throw new InvalidOperationException("游戏正在运行，请先关闭游戏");
        }

        var mods = GetMods(game);
        var oldEnabled = mod.Enabled;
        var oldMappings = new Dictionary<string, string>(mod.OverwriteFiles, StringComparer.OrdinalIgnoreCase);
        var backups = new BackupStore(game);
        try
        {
            mod.Enabled = enable;
            if (enable && game.UsesPakPatches && _settings.Current.FixPakNumber)
            {
                PakAllocator.Assign(game, gamePath, mods, mod, enable);
            }

            ValidateEnabledMods(game, mods, mod, enable);
            if (!enable)
            {
                Deploy(game, gamePath, mod, enable: false, backups);
                if (game.UsesPakPatches)
                {
                    PakAllocator.Clear(mod);
                }
            }

            ModRepository.Save(game, mod);
            var related = FindOverlapping(game, mods, mod, onlyEnabled: true, includeSelf: true);
            related.Sort((left, right) => GroupOrder.Compare(left, right, GetGroups(game)));

            foreach (var item in related)
            {
                if (item == mod && !enable)
                {
                    continue;
                }

                Deploy(game, gamePath, item, enable: true, backups);
            }

            if (!enable)
            {
                CleanupEmpty(game, gamePath);
            }
        }
        catch
        {
            var attemptedMappings = new Dictionary<string, string>(mod.OverwriteFiles, StringComparer.OrdinalIgnoreCase);
            if (!oldEnabled)
            {
                try
                {
                    mod.OverwriteFiles.Clear();
                    foreach (var mapping in attemptedMappings)
                    {
                        mod.OverwriteFiles[mapping.Key] = mapping.Value;
                    }

                    Deploy(game, gamePath, mod, enable: false, backups);
                }
                catch
                {
                }
            }

            mod.Enabled = oldEnabled;
            mod.OverwriteFiles.Clear();
            foreach (var mapping in oldMappings)
            {
                mod.OverwriteFiles[mapping.Key] = mapping.Value;
            }

            ModRepository.Save(game, mod);
            try
            {
                RedeployEnabled(game, gamePath, mods, backups);
            }
            catch
            {
            }

            throw;
        }
    }

    public void Uninstall(GameProfile game, ModRecord mod)
    {
        if (mod.Enabled)
        {
            SetEnabled(game, mod, false);
        }

        var mods = GetMods(game);
        var groups = GetGroups(game);
        mods.Remove(mod);
        ModRepository.Delete(game, mod);
        ModRepository.FixIndex(game, mods, groups);
    }

    public void Move(GameProfile game, ModRecord mod, int delta)
    {
        var mods = GetMods(game);
        var groups = GetGroups(game);
        var members = mods.Where(item => item.GroupId == mod.GroupId).OrderBy(item => item.Index).ToList();
        var index = members.IndexOf(mod);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= members.Count)
        {
            return;
        }

        (members[index].Index, members[target].Index) = (members[target].Index, members[index].Index);
        ModRepository.Save(game, members[index]);
        ModRepository.Save(game, members[target]);
        ModRepository.FixIndex(game, mods, groups);
        RedeployIfNeeded(game, mods);
    }

    public ModGroup CreateGroup(GameProfile game, string name)
    {
        var groups = GetGroups(game);
        var defaultGroup = groups.First(group => group.IsDefault);
        var group = new ModGroup
        {
            Id = groups.Max(item => item.Id) + 1,
            Name = string.IsNullOrWhiteSpace(name) ? $"分组 {groups.Count}" : name.Trim(),
            Index = defaultGroup.Index,
            IsDefault = false
        };
        defaultGroup.Index++;
        groups.Add(group);
        ModRepository.FixGroupIndex(game, groups);
        return group;
    }

    public void RenameGroup(GameProfile game, ModGroup group, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("分组名称不能为空");
        }

        group.Name = name.Trim();
        ModRepository.SaveGroups(game, GetGroups(game));
    }

    public void DeleteGroup(GameProfile game, ModGroup group)
    {
        if (group.IsDefault)
        {
            throw new InvalidOperationException("不能删除未分组");
        }

        var groups = GetGroups(game);
        var mods = GetMods(game);
        var defaultGroup = groups.First(item => item.IsDefault);
        var fallbackIndex = mods.Count(item => item.GroupId == defaultGroup.Id);
        foreach (var mod in mods.Where(item => item.GroupId == group.Id).OrderBy(item => item.Index).ToList())
        {
            mod.GroupId = defaultGroup.Id;
            mod.Index = ++fallbackIndex;
            ModRepository.Save(game, mod);
        }

        groups.Remove(group);
        ModRepository.FixGroupIndex(game, groups);
        ModRepository.FixIndex(game, mods, groups);
        RedeployIfNeeded(game, mods);
    }

    public void MoveGroup(GameProfile game, ModGroup group, int delta)
    {
        var groups = GetGroups(game);
        var ordered = groups.OrderBy(item => item.Index).ToList();
        var index = ordered.IndexOf(group);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= ordered.Count)
        {
            return;
        }

        (ordered[index].Index, ordered[target].Index) = (ordered[target].Index, ordered[index].Index);
        ModRepository.FixGroupIndex(game, groups);
        RedeployIfNeeded(game, GetMods(game));
    }

    public void MoveModToGroup(GameProfile game, ModRecord mod, int groupId)
    {
        var groups = GetGroups(game);
        if (mod.GroupId == groupId || groups.All(group => group.Id != groupId))
        {
            return;
        }

        var mods = GetMods(game);
        mod.GroupId = groupId;
        var members = mods.Where(item => item.GroupId == groupId && item != mod).ToList();
        mod.Index = members.Count == 0 ? 1 : members.Max(item => item.Index) + 1;
        ModRepository.Save(game, mod);
        ModRepository.FixIndex(game, mods, groups);
        RedeployIfNeeded(game, mods);
    }

    public void SetGroupEnabled(GameProfile game, ModGroup group, bool enable)
    {
        var gamePath = RequireGamePath(game);
        if (_settings.Current.CheckGameRunning && IsGameRunning(game, gamePath))
        {
            throw new InvalidOperationException("游戏正在运行，请先关闭游戏");
        }

        var mods = GetMods(game);
        var members = mods.Where(item => item.GroupId == group.Id).ToList();
        if (members.Count == 0)
        {
            return;
        }

        ValidateEnabledMods(game, mods, members[0], enable);
        var backups = new BackupStore(game);
        foreach (var mod in GroupOrder.Ordered(members, GetGroups(game)))
        {
            if (mod.Enabled == enable)
            {
                continue;
            }

            if (!enable)
            {
                Deploy(game, gamePath, mod, enable: false, backups);
                if (game.UsesPakPatches)
                {
                    PakAllocator.Clear(mod);
                }
            }

            mod.Enabled = enable;
            if (enable && game.UsesPakPatches && _settings.Current.FixPakNumber)
            {
                PakAllocator.Assign(game, gamePath, mods, mod, enable);
            }

            ModRepository.Save(game, mod);
        }

        RedeployEnabled(game, gamePath, mods, backups);
        if (!enable)
        {
            CleanupEmpty(game, gamePath);
        }
    }

    public IReadOnlyList<ModRecord> FindConflicts(GameProfile game, ModRecord mod)
    {
        var mods = GetMods(game);
        return FindOverlapping(game, mods, mod, onlyEnabled: false, includeSelf: false);
    }

    public void CleanDeployed(GameProfile game, bool all)
    {
        var gamePath = RequireGamePath(game);
        if (_settings.Current.CheckGameRunning && IsGameRunning(game, gamePath))
        {
            throw new InvalidOperationException("游戏正在运行，请先关闭游戏");
        }

        var mods = GetMods(game);
        var backups = new BackupStore(game);
        foreach (var mod in mods.Where(m => m.Enabled).ToList())
        {
            Deploy(game, gamePath, mod, enable: false, backups);
            mod.Enabled = false;
            if (game.UsesPakPatches)
            {
                PakAllocator.Clear(mod);
            }
            ModRepository.Save(game, mod);
        }

        CleanupEmpty(game, gamePath);
    }

    public void Launch(GameProfile game)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://run/{game.SteamAppId}",
            UseShellExecute = true
        });
    }

    public bool IsGameRunning(GameProfile game, string? gamePath = null)
    {
        var name = Path.GetFileNameWithoutExtension(game.ExeName);
        return Process.GetProcessesByName(name).Length > 0;
    }

    public string PreviewPath(GameProfile game, ModRecord mod)
    {
        if (!string.IsNullOrWhiteSpace(mod.PreviewImage) && File.Exists(mod.PreviewImage))
        {
            return mod.PreviewImage;
        }

        var fallback = Path.Combine(AppPaths.ModFilesDir(game.SteamAppId, mod.Id), "screenshot.png");
        return File.Exists(fallback) ? fallback : "";
    }

    private void RedeployIfNeeded(GameProfile game, List<ModRecord> mods)
    {
        if (!mods.Any(item => item.Enabled))
        {
            return;
        }

        var gamePath = RequireGamePath(game);
        if (_settings.Current.CheckGameRunning && IsGameRunning(game, gamePath))
        {
            throw new InvalidOperationException("游戏正在运行，请先关闭游戏");
        }

        ValidateEnabledMods(game, mods, mods.First(item => item.Enabled), enable: true);
        RedeployEnabled(game, gamePath, mods, new BackupStore(game));
    }

    private void RedeployEnabled(GameProfile game, string gamePath, IEnumerable<ModRecord> mods, BackupStore backups)
    {
        foreach (var enabledMod in GroupOrder.Ordered(mods.Where(item => item.Enabled), GetGroups(game)))
        {
            Deploy(game, gamePath, enabledMod, enable: true, backups);
        }
    }

    private void Deploy(GameProfile game, string gamePath, ModRecord mod, bool enable, BackupStore backups)
    {
        var filesDir = AppPaths.ModFilesDir(game.SteamAppId, mod.Id);
        foreach (var relative in mod.Files)
        {
            var destRelative = mod.DeployPath(relative);
            if (!ModLayoutParser.IsSafeStoredPath(relative) ||
                !ModLayoutParser.IsSafeDeploymentPath(game, destRelative))
            {
                throw new InvalidOperationException($"已阻止不安全的 MOD 部署路径: {destRelative}");
            }

            var dest = Path.Combine(gamePath, destRelative.Replace('/', Path.DirectorySeparatorChar));
            if (!enable)
            {
                if (File.Exists(dest))
                {
                    File.Delete(dest);
                }

                var hasOtherOwner = GetMods(game).Any(other => other != mod && other.Enabled &&
                    other.Files.Any(file => string.Equals(other.DeployPath(file), destRelative, StringComparison.OrdinalIgnoreCase)));
                backups.OnRemove(dest, destRelative, hasOtherOwner);
                continue;
            }

            var source = Path.Combine(filesDir, relative.Replace('/', Path.DirectorySeparatorChar));
            var filesRoot = Path.GetFullPath(filesDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(source).StartsWith(filesRoot, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(source))
            {
                throw new InvalidOperationException($"MOD 文件缺失: {relative}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (enable && destRelative.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(dest) && !FilesEqual(source, dest))
            {
                throw new InvalidOperationException($"检测到 PAK 文件冲突，已阻止覆盖游戏文件: {destRelative}");
            }

            backups.OnDeploy(gamePath, destRelative, dest);
            CopyVerified(source, dest);
        }
    }

    private List<ModRecord> FindOverlapping(GameProfile game, IEnumerable<ModRecord> mods, ModRecord target, bool onlyEnabled, bool includeSelf)
    {
        var targetFiles = new HashSet<string>(target.Files.Select(target.DeployPath), StringComparer.OrdinalIgnoreCase);
        var result = new List<ModRecord>();
        foreach (var mod in mods)
        {
            if (mod == target)
            {
                if (includeSelf && (!onlyEnabled || mod.Enabled))
                {
                    result.Add(mod);
                }

                continue;
            }

            if (onlyEnabled && !mod.Enabled)
            {
                continue;
            }

            if (mod.Files.Select(mod.DeployPath).Any(targetFiles.Contains))
            {
                result.Add(mod);
            }
        }

        return result;
    }

    private static void ValidateParsedMod(GameProfile game, ParsedMod parsed)
    {
        if (string.IsNullOrWhiteSpace(parsed.StagingDir) || !Directory.Exists(parsed.StagingDir))
        {
            throw new InvalidOperationException("MOD 临时目录不存在，无法保证文件完整性");
        }

        var stagingRoot = Path.GetFullPath(parsed.StagingDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in parsed.Files)
        {
            var source = Path.GetFullPath(file.SourcePath);
            if (!source.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
            {
                throw new InvalidOperationException($"MOD 文件缺失或位于临时目录之外: {file.SourcePath}");
            }

            var isUnmappedPak = game.UsesPakPatches && ModLayoutParser.IsPakFile(file.RelativeDest);
            if (!ModLayoutParser.IsSafeStoredPath(file.RelativeDest) ||
                (!isUnmappedPak && !ModLayoutParser.IsSafeDeploymentPath(game, file.RelativeDest)))
            {
                throw new InvalidOperationException($"MOD 包含不允许部署到游戏目录的文件: {file.RelativeDest}");
            }

            var destination = file.RelativeDest.Replace('\\', '/').TrimEnd('/');
            if (!destinations.Add(destination))
            {
                throw new InvalidOperationException($"MOD 中存在重复目标文件: {destination}");
            }
        }

        if (destinations.Count == 0)
        {
            throw new InvalidOperationException("MOD 没有可部署文件");
        }
    }

    private static void ValidateEnabledMods(GameProfile game, IEnumerable<ModRecord> mods, ModRecord target, bool enable)
    {
        foreach (var mod in mods)
        {
            if (!mod.Enabled || (!enable && mod == target))
            {
                continue;
            }

            var filesRoot = Path.GetFullPath(AppPaths.ModFilesDir(game.SteamAppId, mod.Id))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            foreach (var file in mod.Files)
            {
                var source = Path.GetFullPath(Path.Combine(filesRoot, file.Replace('/', Path.DirectorySeparatorChar)));
                if (!source.StartsWith(filesRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
                {
                    throw new InvalidOperationException($"MOD 文件缺失: {mod.DisplayName} / {file}");
                }

                if (!ModLayoutParser.IsSafeStoredPath(file) ||
                    !ModLayoutParser.IsSafeDeploymentPath(game, mod.DeployPath(file)))
                {
                    throw new InvalidOperationException($"已阻止不安全的 MOD 部署路径: {mod.DeployPath(file)}");
                }
            }
        }
    }

    private static void CopyVerified(string source, string destination)
    {
        File.Copy(source, destination, true);
        if (new FileInfo(source).Length != new FileInfo(destination).Length)
        {
            throw new IOException($"文件复制不完整: {source}");
        }
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        var leftBuffer = new byte[1024 * 1024];
        var rightBuffer = new byte[1024 * 1024];
        int leftRead;
        while ((leftRead = leftStream.Read(leftBuffer, 0, leftBuffer.Length)) > 0)
        {
            var rightRead = rightStream.Read(rightBuffer, 0, rightBuffer.Length);
            if (leftRead != rightRead || !leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
            {
                return false;
            }
        }

        return rightStream.ReadByte() == -1;
    }

    private string RequireGamePath(GameProfile game)
    {
        var path = ResolveGamePath(game);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"尚未设置 {game.DisplayName} 的游戏目录");
        }

        return path;
    }

    private string KeepSource(string source, string name)
    {
        if (!File.Exists(source) || Directory.Exists(source))
        {
            return source;
        }

        if (_settings.Current.InstallOption == 0)
        {
            return source;
        }

        Directory.CreateDirectory(AppPaths.DownloadDir);
        var dest = Path.Combine(AppPaths.DownloadDir, Sanitize(name) + Path.GetExtension(source));
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        if (File.Exists(dest))
        {
            File.Move(dest, dest + ".bak", true);
        }

        if (_settings.Current.InstallOption == 2)
        {
            File.Move(source, dest, true);
        }
        else
        {
            File.Copy(source, dest, true);
        }

        return dest;
    }

    private static void CleanupEmpty(GameProfile game, string gamePath)
    {
        if (game.UsesReFramework)
        {
            DeleteEmpty(Path.Combine(gamePath, "natives"));
            DeleteEmpty(Path.Combine(gamePath, "reframework"));
        }
        else
        {
            DeleteEmpty(Path.Combine(gamePath, "nativePC"));
        }
    }

    private static void DeleteEmpty(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var child in Directory.GetDirectories(dir))
        {
            DeleteEmpty(child);
        }

        if (Directory.GetFileSystemEntries(dir).Length == 0)
        {
            Directory.Delete(dir);
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
