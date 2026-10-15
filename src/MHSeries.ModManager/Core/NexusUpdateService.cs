using MhModManager.Models;

namespace MhModManager.Core;

public sealed record NexusUpdateInfo(
    bool Available,
    string CurrentVersion,
    string LatestVersion,
    long LatestFileId,
    string LatestFileName,
    long LatestFileSize,
    DateTimeOffset LatestUploadedAt,
    IReadOnlyList<NexusModFile> Files);

/// <summary>Nexus 更新编排:文件列表内存缓存 + 检查 + 下载并原地更新。HTTP 通过 INexusApi 注入以便离线测试。</summary>
public sealed class NexusUpdateService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly SettingsStore _settings;
    private readonly INexusApi _api;
    private readonly ModService _mods;
    private readonly Dictionary<(GameId, int), (DateTimeOffset FetchedAt, NexusCheckResult Result)> _cache = [];

    public NexusUpdateService(SettingsStore settings, ModService mods, INexusApi? api = null)
    {
        _settings = settings;
        _mods = mods;
        _api = api ?? new NexusApiClient();
    }

    public string RequireApiKey()
    {
        var key = _settings.Current.NexusApiKey?.Trim() ?? "";
        if (key.Length == 0)
        {
            throw new InvalidOperationException("尚未设置 Nexus API Key，请先在设置中填写");
        }

        return key;
    }

    public Task<NexusUserInfo> ValidateAsync(CancellationToken cancellation = default) =>
        _api.ValidateAsync(RequireApiKey(), cancellation);

    public async Task<NexusUpdateInfo> CheckAsync(GameProfile game, ModRecord mod, bool force, CancellationToken cancellation = default)
    {
        if (mod.NexusId <= 0)
        {
            throw new InvalidOperationException("这个 MOD 没有识别到 Nexus 来源，无法检查更新");
        }

        var key = RequireApiKey();
        var cacheKey = (game.Id, mod.NexusId);
        if (!force && _cache.TryGetValue(cacheKey, out var cached) &&
            DateTimeOffset.Now - cached.FetchedAt < CacheTtl)
        {
            return BuildInfo(mod, cached.Result);
        }

        var result = await _api.GetFilesAsync(key, game.NexusSlug, mod.NexusId, cancellation);
        _cache[cacheKey] = (DateTimeOffset.Now, result);
        return BuildInfo(mod, result);
    }

    /// <summary>下载 Nexus 文件到 downloads/ 并原地更新 MOD。文件已存在时不重复下载(密码重试路径复用)。</summary>
    public async Task<ImportBatch> DownloadAndUpdateAsync(
        GameProfile game,
        ModRecord mod,
        long fileId,
        IReadOnlyList<string>? passwords = null,
        CancellationToken cancellation = default)
    {
        if (mod.NexusId <= 0)
        {
            throw new InvalidOperationException("这个 MOD 没有识别到 Nexus 来源");
        }

        var key = RequireApiKey();
        var files = await GetFilesAsync(key, game, mod, cancellation);
        var file = files.FirstOrDefault(item => item.FileId == fileId)
            ?? throw new InvalidOperationException("Nexus 文件不存在，请重新检查更新");

        AppPaths.EnsureCreated();
        var dest = Path.Combine(AppPaths.DownloadDir, NexusUpdateLogic.SafeFileName(file.FileName));
        if (!File.Exists(dest))
        {
            var url = await _api.GetDownloadLinkAsync(key, game.NexusSlug, mod.NexusId, fileId, cancellation);
            await _api.DownloadToFileAsync(url, dest, cancellation);
        }

        return await Task.Run(() => _mods.Update(game, mod, dest, null, passwords), cancellation);
    }

    private async Task<IReadOnlyList<NexusModFile>> GetFilesAsync(string key, GameProfile game, ModRecord mod, CancellationToken cancellation)
    {
        var cacheKey = (game.Id, mod.NexusId);
        if (_cache.TryGetValue(cacheKey, out var cached) && DateTimeOffset.Now - cached.FetchedAt < CacheTtl)
        {
            return cached.Result.Files;
        }

        var result = await _api.GetFilesAsync(key, game.NexusSlug, mod.NexusId, cancellation);
        _cache[cacheKey] = (DateTimeOffset.Now, result);
        return result.Files;
    }

    private static NexusUpdateInfo BuildInfo(ModRecord mod, NexusCheckResult result)
    {
        var recommended = NexusUpdateLogic.PickRecommended(result.Files);
        var available = recommended is not null && NexusUpdateLogic.HasUpdate(mod.Version, mod.InstallTime, recommended);
        return new NexusUpdateInfo(
            available,
            mod.Version,
            recommended?.Version ?? "",
            recommended?.FileId ?? 0,
            recommended?.FileName ?? "",
            recommended?.SizeBytes ?? 0,
            recommended?.UploadedAt ?? DateTimeOffset.MinValue,
            result.Files);
    }
}
