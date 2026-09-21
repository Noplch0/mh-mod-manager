using System.Diagnostics;
using MhModManager.Core.Equipment;
using MhModManager.Models;

namespace MhModManager.Core;

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
            ModRepository.FixIndex(game, mods, groups);
            BundleMigration.Migrate(game, mods, groups);
            _groups[game.Id] = groups;
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
        var units = ParseImportUnits(game, path, progress, out _);
        return units.Count == 1
            ? units[0]
            : throw new InvalidOperationException("这个压缩包包含多个独立 MOD，请使用批量导入");
    }

    public ImportBatch Import(GameProfile game, string path, IProgress<int>? progress = null)
    {
        var units = ParseImportUnits(game, path, progress, out var bundleName);
        if (units.Count == 0)
        {
            throw new InvalidOperationException("未能识别这个 MOD 的文件结构");
        }

        if (units.Count == 1)
        {
            return new ImportBatch { Mods = [Install(game, units[0], null, keepSource: true)] };
        }

        return new ImportBatch { Mods = [InstallBundle(game, path, units, bundleName)] };
    }

    public ImportBatch Update(GameProfile game, ModRecord existing, string path, IProgress<int>? progress = null)
    {
        var units = ParseImportUnits(game, path, progress, out var bundleName);
        if (units.Count == 0)
        {
            throw new InvalidOperationException("未能识别这个 MOD 的文件结构");
        }

        if (units.Count == 1)
        {
            Replace(game, existing, units[0]);
            return new ImportBatch { Mods = [existing] };
        }

        ReplaceBundle(game, existing, path, units, bundleName);
        return new ImportBatch { Mods = [existing] };
    }

    public void Replace(GameProfile game, ModRecord existing, ParsedMod parsed)
    {
        ValidateParsedMod(game, parsed);
        var wasEnabled = existing.Enabled;
        if (wasEnabled)
        {
            SetEnabled(game, existing, false);
        }

        var destDir = AppPaths.ModFilesDir(game.SteamAppId, existing.Id);
        if (Directory.Exists(destDir))
        {
            Directory.Delete(destDir, true);
        }

        var oldCovers = Path.Combine(AppPaths.ModDir(game.SteamAppId, existing.Id), "covers");
        if (Directory.Exists(oldCovers))
        {
            Directory.Delete(oldCovers, true);
        }

        try
        {
            Directory.CreateDirectory(destDir);
            existing.Files.Clear();
            existing.OverwriteFiles.Clear();
            existing.Components.Clear();
            existing.Name = parsed.Name;
            if (string.IsNullOrWhiteSpace(existing.DisplayName))
            {
                existing.DisplayName = string.IsNullOrWhiteSpace(parsed.Name)
                    ? Path.GetFileNameWithoutExtension(parsed.SourceFile)
                    : parsed.Name;
            }
            existing.Version = parsed.Version;
            existing.Author = parsed.Author;
            existing.Category = parsed.Category;
            existing.HomeUrl = parsed.HomeUrl;
            existing.NexusId = parsed.NexusId;
            existing.InstallTime = DateTimeOffset.Now;
            existing.PreviewImage = "";
            if (existing.NexusId > 0 && string.IsNullOrWhiteSpace(existing.HomeUrl))
            {
                existing.HomeUrl = NexusNames.GetNexusUrl(game, existing.NexusId);
            }

            foreach (var file in parsed.Files)
            {
                var dest = Path.Combine(destDir, file.RelativeDest.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                CopyVerified(file.SourcePath, dest);
                existing.Files.Add(file.RelativeDest);
            }

            if (!string.IsNullOrWhiteSpace(parsed.PreviewSource) && File.Exists(parsed.PreviewSource))
            {
                var ext = Path.GetExtension(parsed.PreviewSource);
                if (!IsPreviewExtension(ext))
                {
                    ext = ".png";
                }

                var preview = Path.Combine(destDir, "screenshot" + ext.ToLowerInvariant());
                File.Copy(parsed.PreviewSource, preview, true);
                existing.PreviewImage = preview;
            }

            existing.SourceFile = KeepSource(parsed.SourceFile, parsed.Name);
            ModRepository.Save(game, existing);
        }
        catch
        {
            throw;
        }
        finally
        {
            TryDelete(parsed.StagingDir);
        }

        if (wasEnabled)
        {
            SetEnabled(game, existing, true);
        }
    }

    /// <summary>多单元压缩包导入为一个组件化 MOD：组件 = 顶层文件夹 / 散装 pak。</summary>
    private ModRecord InstallBundle(GameProfile game, string sourcePath, List<ParsedMod> units, string bundleName)
    {
        foreach (var unit in units)
        {
            ValidateParsedMod(game, unit);
        }

        var mods = GetMods(game);
        var groups = GetGroups(game);
        var record = new ModRecord
        {
            Name = bundleName,
            DisplayName = BundleDisplayName(units, bundleName),
            Version = units.Select(unit => unit.Version).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "",
            Author = units.Select(unit => unit.Author).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "",
            Category = units.Select(unit => unit.Category).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "",
            NexusId = units.Select(unit => unit.NexusId).FirstOrDefault(id => id > 0),
            InstallTime = DateTimeOffset.Now
        };
        if (record.NexusId > 0)
        {
            record.HomeUrl = NexusNames.GetNexusUrl(game, record.NexusId);
        }

        ModRepository.MakeId(mods, groups, record);
        var filesDir = AppPaths.ModFilesDir(game.SteamAppId, record.Id);
        var coversDir = Path.Combine(AppPaths.ModDir(game.SteamAppId, record.Id), "covers");
        try
        {
            Directory.CreateDirectory(filesDir);
            Directory.CreateDirectory(coversDir);
            var nextId = 1;
            foreach (var unit in units)
            {
                var component = new ModComponent { Id = nextId++, Name = unit.Name, Enabled = false };
                CopyComponentFiles(unit, component, filesDir, coversDir, record);
                record.Components.Add(component);
            }

            record.SourceFile = KeepSource(sourcePath, record.DisplayName);
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
            foreach (var unit in units)
            {
                TryDelete(unit.StagingDir);
            }
        }
    }

    /// <summary>用新压缩包原地重建组件化 MOD（普通 MOD 转组件化也走这里）。</summary>
    private void ReplaceBundle(GameProfile game, ModRecord existing, string sourcePath, List<ParsedMod> units, string bundleName)
    {
        foreach (var unit in units)
        {
            ValidateParsedMod(game, unit);
        }

        var wasEnabled = existing.Enabled;
        if (wasEnabled)
        {
            SetEnabled(game, existing, false);
        }

        var modDir = AppPaths.ModDir(game.SteamAppId, existing.Id);
        var filesDir = AppPaths.ModFilesDir(game.SteamAppId, existing.Id);
        var coversDir = Path.Combine(modDir, "covers");
        try
        {
            if (Directory.Exists(filesDir))
            {
                Directory.Delete(filesDir, true);
            }

            if (Directory.Exists(coversDir))
            {
                Directory.Delete(coversDir, true);
            }

            Directory.CreateDirectory(filesDir);

            // 按组件名保留旧的开关状态，没匹配上的保持关闭。
            var previousByName = existing.Components
                .Where(component => !string.IsNullOrWhiteSpace(component.Name))
                .GroupBy(component => component.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Enabled, StringComparer.OrdinalIgnoreCase);

            existing.Files.Clear();
            existing.OverwriteFiles.Clear();
            existing.Components.Clear();
            existing.Name = bundleName;
            if (string.IsNullOrWhiteSpace(existing.DisplayName))
            {
                existing.DisplayName = BundleDisplayName(units, bundleName);
            }

            existing.Version = units.Select(unit => unit.Version).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
            existing.Author = units.Select(unit => unit.Author).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
            existing.Category = units.Select(unit => unit.Category).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
            existing.NexusId = units.Select(unit => unit.NexusId).FirstOrDefault(id => id > 0);
            existing.HomeUrl = "";
            existing.InstallTime = DateTimeOffset.Now;
            existing.PreviewImage = "";
            if (existing.NexusId > 0)
            {
                existing.HomeUrl = NexusNames.GetNexusUrl(game, existing.NexusId);
            }

            Directory.CreateDirectory(coversDir);
            var nextId = 1;
            foreach (var unit in units)
            {
                var component = new ModComponent { Id = nextId++, Name = unit.Name };
                component.Enabled = previousByName.TryGetValue(component.Name, out var enabled) && enabled;
                CopyComponentFiles(unit, component, filesDir, coversDir, existing);
                existing.Components.Add(component);
            }

            existing.SourceFile = KeepSource(sourcePath, existing.DisplayName);
            ModRepository.Save(game, existing);
        }
        finally
        {
            foreach (var unit in units)
            {
                TryDelete(unit.StagingDir);
            }
        }

        if (wasEnabled)
        {
            SetEnabled(game, existing, true);
        }
    }

    private static void CopyComponentFiles(ParsedMod unit, ModComponent component, string filesDir, string coversDir, ModRecord record)
    {
        var prefix = $"c{component.Id}/";
        foreach (var file in unit.Files)
        {
            var stored = prefix + file.RelativeDest;
            var dest = Path.Combine(filesDir, stored.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            CopyVerified(file.SourcePath, dest);
            component.Files.Add(stored);
            record.Files.Add(stored);
        }

        if (!string.IsNullOrWhiteSpace(unit.PreviewSource) && File.Exists(unit.PreviewSource))
        {
            var ext = Path.GetExtension(unit.PreviewSource);
            if (!IsPreviewExtension(ext))
            {
                ext = ".png";
            }

            var cover = Path.Combine(coversDir, $"c{component.Id}{ext.ToLowerInvariant()}");
            File.Copy(unit.PreviewSource, cover, true);
            if (string.IsNullOrEmpty(record.PreviewImage))
            {
                record.PreviewImage = cover;
            }
        }
    }

    private static string BundleDisplayName(List<ParsedMod> units, string bundleName)
    {
        var fromIni = units.Select(unit => unit.BundleName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(fromIni))
        {
            return fromIni.Trim();
        }

        var parsed = NexusNames.ParseNexusDownloadStem(bundleName) ?? NexusNames.ParseNexusStem(bundleName);
        var name = parsed?.Name ?? bundleName;
        return string.IsNullOrWhiteSpace(name) ? "组件化 MOD" : name.Trim();
    }

    public List<ParsedMod> ParseImportUnits(GameProfile game, string path, IProgress<int>? progress, out string bundleName)
    {
        AppPaths.EnsureCreated();
        bundleName = BundleName(path);
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
            }
            else
            {
                File.Copy(path, Path.Combine(staging, Path.GetFileName(path)), true);
            }

            var units = SplitImportUnits(game, path, staging);
            if (units.Count == 0)
            {
                throw new InvalidOperationException("未能识别这个 MOD 的文件结构");
            }

            return units;
        }
        catch
        {
            TryDelete(staging);
            throw;
        }
    }

    public ModRecord Install(GameProfile game, ParsedMod parsed) => Install(game, parsed, null, true);

    public ModRecord Install(GameProfile game, ParsedMod parsed, int? groupId, bool keepSource)
    {
        ValidateParsedMod(game, parsed);
        var mods = GetMods(game);
        var groups = GetGroups(game);
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

        ModRepository.MakeId(mods, groups, record);
        if (groupId is int targetGroup && groups.Any(item => item.Id == targetGroup))
        {
            var members = mods.Where(item => item.GroupId == targetGroup).ToList();
            record.GroupId = targetGroup;
            record.Index = members.Count == 0 ? 1 : members.Max(item => item.Index) + 1;
        }

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
                var ext = Path.GetExtension(parsed.PreviewSource);
                if (!IsPreviewExtension(ext))
                {
                    ext = ".png";
                }

                var preview = Path.Combine(destDir, "screenshot" + ext.ToLowerInvariant());
                File.Copy(parsed.PreviewSource, preview, true);
                record.PreviewImage = preview;
            }

            record.SourceFile = keepSource
                ? KeepSource(parsed.SourceFile, parsed.Name)
                : parsed.SourceFile;
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
        try
        {
            mod.Enabled = enable;
            if (enable && game.UsesPakPatches)
            {
                PakModsManager.AssignForEnable(game, gamePath, mods, GetGroups(game));
            }

            ValidateEnabledMods(game, mods, mod, enable);
            if (!enable)
            {
                Deploy(game, gamePath, mod, enable: false);
                if (game.UsesPakPatches)
                {
                    // 重新排布其余启用 MOD 的 X 编号。
                    PakModsManager.AssignForEnable(game, gamePath, mods, GetGroups(game));
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

                Deploy(game, gamePath, item, enable: true);
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

                    Deploy(game, gamePath, mod, enable: false);
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
                RedeployEnabled(game, gamePath, mods);
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

    public void RenameMod(GameProfile game, ModRecord mod, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("MOD 名称不能为空");
        }

        mod.DisplayName = name.Trim();
        ModRepository.Save(game, mod);
        if (mod.Enabled && game.UsesPakPatches)
        {
            var gamePath = RequireGamePath(game);
            PakModsManager.Sync(game, gamePath, GetMods(game), GetGroups(game));
            Deploy(game, gamePath, mod, enable: true);
        }
    }

    public void ChangeEquipment(GameProfile game, ModRecord mod, string kindName, int fromId, int toId, bool isPfb, bool withTex)
    {
        if (mod.Enabled)
        {
            throw new InvalidOperationException("请先禁用 MOD 再修改对应装备");
        }

        if (!Enum.TryParse<EquipKind>(kindName, true, out var kind) || !Enum.IsDefined(typeof(EquipKind), kind))
        {
            throw new InvalidOperationException("无法识别装备类型");
        }

        var filesDir = AppPaths.ModFilesDir(game.SteamAppId, mod.Id);
        var changed = 0;
        if (mod.IsBundle)
        {
            // 组件化记录：逐个组件目录运行改写，组件内的路径不带前缀才能命中装备模式。
            foreach (var component in mod.Components)
            {
                var prefix = $"c{component.Id}/";
                var componentFiles = component.Files
                    .Where(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(file => file[prefix.Length..])
                    .ToList();
                changed += EquipmentRemapper.Apply(game.Id, Path.Combine(filesDir, prefix), componentFiles,
                    kind, fromId, toId, isPfb, withTex);
            }
        }
        else
        {
            changed = EquipmentRemapper.Apply(game.Id, filesDir, mod.Files, kind, fromId, toId, isPfb, withTex);
        }

        if (changed <= 0)
        {
            if (fromId == toId)
            {
                return;
            }

            throw new InvalidOperationException("没有可修改的装备文件");
        }

        ModRepository.RefreshFiles(game, mod);
        ModRepository.Save(game, mod);
    }

    /// <summary>组件化 MOD 内单个组件的开关；总开关关闭时只改状态。</summary>
    public void SetComponentEnabled(GameProfile game, ModRecord mod, int componentId, bool enable)
    {
        var component = mod.Components.FirstOrDefault(item => item.Id == componentId)
            ?? throw new InvalidOperationException("组件不存在");
        if (component.Enabled == enable)
        {
            return;
        }

        if (!mod.Enabled)
        {
            component.Enabled = enable;
            ModRepository.Save(game, mod);
            return;
        }

        var gamePath = RequireGamePath(game);
        if (_settings.Current.CheckGameRunning && IsGameRunning(game, gamePath))
        {
            throw new InvalidOperationException("游戏正在运行，请先关闭游戏");
        }

        var mods = GetMods(game);
        var oldEnabled = component.Enabled;
        try
        {
            var affected = component.Files.ToList();
            component.Enabled = enable;
            ModRepository.Save(game, mod);
            if (game.UsesPakPatches)
            {
                PakModsManager.AssignForEnable(game, gamePath, mods, GetGroups(game));
            }

            RedeployScoped(game, gamePath, mods, mod, affected);
        }
        catch
        {
            component.Enabled = oldEnabled;
            ModRepository.Save(game, mod);
            try
            {
                RedeployEnabled(game, gamePath, mods);
            }
            catch
            {
            }

            throw;
        }
    }

    /// <summary>调整组件顺序；覆盖规则为列表靠后覆盖靠前，调序后立即按新顺序重部署相交文件。</summary>
    public void MoveComponent(GameProfile game, ModRecord mod, int componentId, int delta)
    {
        var index = mod.Components.FindIndex(item => item.Id == componentId);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= mod.Components.Count)
        {
            return;
        }

        (mod.Components[index], mod.Components[target]) = (mod.Components[target], mod.Components[index]);
        ModRepository.Save(game, mod);
        if (!mod.Enabled)
        {
            return;
        }

        var gamePath = RequireGamePath(game);
        if (_settings.Current.CheckGameRunning && IsGameRunning(game, gamePath))
        {
            throw new InvalidOperationException("游戏正在运行，请先关闭游戏");
        }

        var mods = GetMods(game);
        if (game.UsesPakPatches)
        {
            PakModsManager.AssignForEnable(game, gamePath, mods, GetGroups(game));
        }

        RedeployScoped(game, gamePath, mods, mod,
            mod.Components[index].Files.Concat(mod.Components[target].Files));
    }

    /// <summary>
    /// 范围重部署：删除受影响部署路径上的游戏文件，再按有效优先级
    /// （分组顺序 → 组内顺序 → 组件顺序）重部署文件集与其相交的启用 MOD。
    /// </summary>
    private void RedeployScoped(GameProfile game, string gamePath, List<ModRecord> mods, ModRecord changed, IEnumerable<string> affectedStored)
    {
        var affected = new HashSet<string>(
            affectedStored.Select(changed.DeployPath).Select(path => path.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);

        foreach (var destRelative in affected)
        {
            if (!PakModsManager.IsManagedTarget(destRelative) &&
                !ModLayoutParser.IsSafeDeploymentPath(game, destRelative))
            {
                continue;
            }

            var dest = Path.Combine(gamePath, destRelative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }
        }

        foreach (var enabled in GroupOrder.Ordered(mods.Where(item => item.Enabled), GetGroups(game)))
        {
            var intersects = EnabledStoredFiles(enabled)
                .Select(enabled.DeployPath)
                .Select(path => path.Replace('\\', '/'))
                .Any(affected.Contains);
            if (intersects)
            {
                Deploy(game, gamePath, enabled, enable: true);
            }
        }

        CleanupEmpty(game, gamePath);
    }

    /// <summary>部署视角的存储文件：组件化记录只含启用组件的文件（按组件顺序）。</summary>
    private static IEnumerable<string> EnabledStoredFiles(ModRecord mod) =>
        mod.IsBundle
            ? mod.Components.Where(component => component.Enabled).SelectMany(component => component.Files)
            : mod.Files;

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

    public void MoveModToGroup(GameProfile game, ModRecord mod, int groupId) =>
        MoveModsToGroup(game, [mod], groupId);

    public void MoveModsToGroup(GameProfile game, IReadOnlyList<ModRecord> records, int groupId)
    {
        var groups = GetGroups(game);
        if (records.Count == 0 || groups.All(group => group.Id != groupId))
        {
            return;
        }

        var moving = records.Where(item => item.GroupId != groupId).ToList();
        if (moving.Count == 0)
        {
            return;
        }

        var requiresRedeploy = moving.Any(item => item.Enabled);

        var mods = GetMods(game);
        var members = mods.Where(item => item.GroupId == groupId && !moving.Contains(item)).ToList();
        var nextIndex = members.Count == 0 ? 1 : members.Max(item => item.Index) + 1;
        foreach (var mod in moving)
        {
            mod.GroupId = groupId;
            mod.Index = nextIndex++;
            ModRepository.Save(game, mod);
        }

        ModRepository.FixIndex(game, mods, groups);
        if (requiresRedeploy)
        {
            RedeployIfNeeded(game, mods);
        }
    }

    public void SetGroupCollapsed(GameProfile game, ModGroup group, bool collapsed)
    {
        group.Collapsed = collapsed;
        ModRepository.SaveGroups(game, GetGroups(game));
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
        foreach (var mod in GroupOrder.Ordered(members, GetGroups(game)))
        {
            if (mod.Enabled == enable)
            {
                continue;
            }

            if (!enable)
            {
                Deploy(game, gamePath, mod, enable: false);
                if (game.UsesPakPatches)
                {
                    PakModsManager.AssignForEnable(game, gamePath, mods, GetGroups(game));
                }
            }

            mod.Enabled = enable;
            if (enable && game.UsesPakPatches)
            {
                PakModsManager.AssignForEnable(game, gamePath, mods, GetGroups(game));
            }

            ModRepository.Save(game, mod);
        }

        RedeployEnabled(game, gamePath, mods);
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
        foreach (var mod in mods.Where(m => m.Enabled).ToList())
        {
            Deploy(game, gamePath, mod, enable: false);
            mod.Enabled = false;
            if (game.UsesPakPatches)
            {
                PakModsManager.AssignForEnable(game, gamePath, mods, GetGroups(game));
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

        var filesDir = AppPaths.ModFilesDir(game.SteamAppId, mod.Id);
        if (Directory.Exists(filesDir))
        {
            foreach (var file in Directory.GetFiles(filesDir, "*", SearchOption.TopDirectoryOnly))
            {
                if (ModRepository.IsPreviewFileName(Path.GetFileName(file)))
                {
                    return file;
                }
            }
        }

        return "";
    }

    public string ComponentPreviewPath(GameProfile game, ModRecord mod, int componentId)
    {
        var coversDir = Path.Combine(AppPaths.ModDir(game.SteamAppId, mod.Id), "covers");
        return Directory.Exists(coversDir)
            ? Directory.GetFiles(coversDir, $"c{componentId}.*").FirstOrDefault() ?? ""
            : "";
    }

    private static bool IsPreviewExtension(string ext) =>
        ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);

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
        if (game.UsesPakPatches)
        {
            PakModsManager.Sync(game, gamePath, mods, GetGroups(game));
        }
        RedeployEnabled(game, gamePath, mods);
    }

    private void RedeployEnabled(GameProfile game, string gamePath, IEnumerable<ModRecord> mods)
    {
        foreach (var enabledMod in GroupOrder.Ordered(mods.Where(item => item.Enabled), GetGroups(game)))
        {
            Deploy(game, gamePath, enabledMod, enable: true);
        }
    }

    private void Deploy(GameProfile game, string gamePath, ModRecord mod, bool enable)
    {
        var filesDir = AppPaths.ModFilesDir(game.SteamAppId, mod.Id);
        if (!enable)
        {
            Undeploy(game, gamePath, mod, filesDir);
            return;
        }

        foreach (var relative in EnabledStoredFiles(mod))
        {
            var destRelative = mod.DeployPath(relative);
            var isPakModsTarget = PakModsManager.IsManagedTarget(destRelative);
            if (game.UsesPakPatches &&
                destRelative.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) &&
                !isPakModsTarget)
            {
                throw new InvalidOperationException($"MOD 的 PAK 文件只能部署到 pak_mods 文件夹: {destRelative}");
            }

            if (!ModLayoutParser.IsSafeStoredPath(relative) ||
                (!isPakModsTarget && !ModLayoutParser.IsSafeDeploymentPath(game, destRelative)))
            {
                throw new InvalidOperationException($"已阻止不安全的 MOD 部署路径: {destRelative}");
            }

            var dest = Path.Combine(gamePath, destRelative.Replace('/', Path.DirectorySeparatorChar));
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
            CopyVerified(source, dest);
        }
    }

    private void Undeploy(GameProfile game, string gamePath, ModRecord mod, string filesDir)
    {
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void AddTarget(string destRelative, string source)
        {
            if (string.IsNullOrWhiteSpace(destRelative))
            {
                return;
            }

            var key = destRelative.Replace('\\', '/');
            targets.TryAdd(key, source);
        }

        foreach (var relative in mod.Files)
        {
            var source = Path.Combine(filesDir, relative.Replace('/', Path.DirectorySeparatorChar));
            var destRelative = mod.DeployPath(relative);
            var isPak = PakAllocator.IsPakStored(mod, relative);
            if (isPak && game.UsesPakPatches)
            {
                // 崛起/荒野的 pak 只删除 pak_mods 托管路径，游戏根目录的 pak 不归管理器管。
                if (PakModsManager.IsManagedTarget(destRelative))
                {
                    AddTarget(destRelative, source);
                }

                // pak_mods 里的历史遗留文件（含映射丢失的情况）按内容哈希找到并清理。
                var dir = PakModsManager.PakModsDir(gamePath);
                if (File.Exists(source) && Directory.Exists(dir))
                {
                    var hash = PakAllocator.PrefixHash(source);
                    foreach (var file in Directory.GetFiles(dir, "*.pak"))
                    {
                        if (PakAllocator.PrefixHash(file) == hash)
                        {
                            AddTarget($"{PakModsManager.DirName}/{Path.GetFileName(file)}", source);
                        }
                    }
                }
            }
            else
            {
                AddTarget(relative, source);
                AddTarget(destRelative, source);
            }
        }

        foreach (var mapping in mod.OverwriteFiles)
        {
            var source = Path.Combine(filesDir, mapping.Key.Replace('/', Path.DirectorySeparatorChar));
            AddTarget(mapping.Value, source);
        }

        foreach (var (destRelative, _) in targets)
        {
            if (!PakModsManager.IsManagedTarget(destRelative) &&
                !ModLayoutParser.IsSafeDeploymentPath(game, destRelative)
                && !ModLayoutParser.IsPakFile(destRelative))
            {
                continue;
            }

            var dest = Path.Combine(gamePath, destRelative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }
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
            foreach (var file in EnabledStoredFiles(mod))
            {
                var source = Path.GetFullPath(Path.Combine(filesRoot, file.Replace('/', Path.DirectorySeparatorChar)));
                if (!source.StartsWith(filesRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
                {
                    throw new InvalidOperationException($"MOD 文件缺失: {mod.DisplayName} / {file}");
                }

                if (!ModLayoutParser.IsSafeStoredPath(file) ||
                    (!PakModsManager.IsManagedTarget(mod.DeployPath(file)) &&
                     !ModLayoutParser.IsSafeDeploymentPath(game, mod.DeployPath(file))))
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

    private List<ParsedMod> SplitImportUnits(GameProfile game, string sourcePath, string staging)
    {
        var root = ArchiveExtractor.UnwrapRoot(staging);
        var candidates = DiscoverImportCandidates(root);
        if (LooksLikeSinglePackage(root) || candidates.Count <= 1)
        {
            ArchiveExtractor.ExtractNested(staging);
            return [ParseUnit(game, sourcePath, staging, "")];
        }

        var units = new List<ParsedMod>();
        foreach (var candidate in candidates)
        {
            var childStaging = Path.Combine(AppPaths.TempDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(childStaging);
            if (Directory.Exists(candidate))
            {
                CopyDirectory(candidate, childStaging);
            }
            else
            {
                File.Copy(candidate, Path.Combine(childStaging, Path.GetFileName(candidate)), true);
            }

            ArchiveExtractor.ExtractNested(childStaging);
            var parsed = TryParseUnit(game, sourcePath, childStaging, UnitName(candidate));
            if (parsed is not null)
            {
                units.Add(parsed);
            }
            else
            {
                TryDelete(childStaging);
            }
        }

        if (units.Count <= 1)
        {
            foreach (var parsed in units)
            {
                TryDelete(parsed.StagingDir);
            }

            ArchiveExtractor.ExtractNested(staging);
            return [ParseUnit(game, sourcePath, staging, "")];
        }

        TryDelete(staging);
        return units;
    }

    private static readonly string[] DeployRootNames =
    [
        "nativePC", "natives", "reframework", "autorun", "plugins",
        "pl", "wp", "weapon", "player", "art", "gamedesign"
    ];

    private static bool LooksLikeSinglePackage(string root)
    {
        if (Directory.GetFiles(root, "ModuleConfig.xml", SearchOption.AllDirectories).Length > 0)
        {
            return true;
        }

        // 根目录的散装 pak 不再强制单包：它本身是一个候选组件，
        // 单独一个 pak 时 candidates.Count == 1 自然走单 MOD 路径。
        return Directory.GetDirectories(root).Any(dir =>
            DeployRootNames.Contains(Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase));
    }

    private static List<string> DiscoverImportCandidates(string root)
    {
        var files = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly);
        var dirs = Directory.GetDirectories(root);
        var archives = files.Where(ArchiveExtractor.IsArchive).ToList();
        var paks = files.Where(file =>
                Path.GetExtension(file).Equals(".pak", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var candidates = new List<string>();
        candidates.AddRange(dirs);
        candidates.AddRange(archives);
        candidates.AddRange(paks);
        return candidates
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ParsedMod ParseUnit(GameProfile game, string sourcePath, string staging, string name)
    {
        var parsed = ModLayoutParser.Parse(game, sourcePath, staging);
        if (parsed.Files.Count == 0)
        {
            TryDelete(staging);
            throw new InvalidOperationException("未能识别这个 MOD 的文件结构");
        }

        // modinfo/ModuleConfig 提供的名称优先；否则用候选目录/文件名。
        var sourceStem = Path.GetFileNameWithoutExtension(sourcePath);
        if (!string.IsNullOrWhiteSpace(name) &&
            (string.IsNullOrWhiteSpace(parsed.Name) || parsed.Name == sourceStem))
        {
            parsed.Name = name;
        }

        return parsed;
    }

    private static ParsedMod? TryParseUnit(GameProfile game, string sourcePath, string staging, string name)
    {
        try
        {
            return ParseUnit(game, sourcePath, staging, name);
        }
        catch
        {
            TryDelete(staging);
            return null;
        }
    }

    private static string BundleName(string path) =>
        Directory.Exists(path)
            ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileNameWithoutExtension(path);

    private static string UnitName(string path)
    {
        var name = Directory.Exists(path)
            ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileNameWithoutExtension(path);
        var parsed = NexusNames.ParseNexusDownloadStem(name) ?? NexusNames.ParseNexusStem(name);
        return parsed?.Name ?? name;
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
