using System.IO.Compression;
using MhModManager.Core;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

/// <summary>Nexus 更新:版本判定纯函数 + 假 INexusApi 下的编排(缓存、下载幂等、更新链路)。</summary>
public sealed class NexusUpdateTests : IDisposable
{
    private readonly int _appId = Random.Shared.Next(9_000_001, 9_900_000);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mh-mod-manager-nexus-tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _settings = new();
    private readonly GameProfile _game;
    private readonly ModService _service;
    private readonly List<string> _downloads = [];

    public NexusUpdateTests()
    {
        _game = new GameProfile(GameId.World, "nexus-test", "test", "test", "test.exe", _appId, "monsterhunterworld", "", 0);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, _game.ExeName), "fake-game");
        _settings.Load();
        _settings.Current.InstallOption = 0;
        _settings.Current.CheckGameRunning = false;
        _settings.Current.NexusApiKey = "test-key";
        _settings.SetGamePath(_game, _root);
        _service = new ModService(_settings);
    }

    [Fact]
    public void NormalizeVersionStripsPrefixAndPadsSegments()
    {
        Assert.Equal(NexusUpdateLogic.NormalizeVersion("v1.2.3"), NexusUpdateLogic.NormalizeVersion("1.2.3"));
        Assert.Equal(NexusUpdateLogic.NormalizeVersion("V2"), NexusUpdateLogic.NormalizeVersion("2"));
        Assert.NotEqual(NexusUpdateLogic.NormalizeVersion("1.2"), NexusUpdateLogic.NormalizeVersion("1.2.1"));
        Assert.Equal("01.0a", NexusUpdateLogic.NormalizeVersion("1.0a"));
    }

    [Fact]
    public void HasUpdateDetectsVersionDifferenceAndReupload()
    {
        var old = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var file = MakeFile(version: "1.0", uploaded: old);

        Assert.True(NexusUpdateLogic.HasUpdate("0.9", old, file));
        Assert.False(NexusUpdateLogic.HasUpdate("1.0", old.AddHours(1), file));
        // 版本相同但文件重新上传 → 有更新
        Assert.True(NexusUpdateLogic.HasUpdate("1.0", old.AddHours(-1), file));
        // 本地版本未知 → 提示用户判断
        Assert.True(NexusUpdateLogic.HasUpdate("", old, file));
    }

    [Fact]
    public void PickRecommendedPrefersMainCategoryThenLatest()
    {
        var old = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var optional = MakeFile(id: 2, version: "3.0", uploaded: old.AddHours(5), category: 3);
        var mainOld = MakeFile(id: 3, version: "1.0", uploaded: old, category: 1);
        var mainNew = MakeFile(id: 4, version: "2.0", uploaded: old.AddHours(1), category: 1);

        var picked = NexusUpdateLogic.PickRecommended([optional, mainOld, mainNew]);

        Assert.Equal(4, picked!.FileId);
        // 没有主分类时回退到任意最新
        Assert.Equal(2, NexusUpdateLogic.PickRecommended([optional])!.FileId);
        Assert.Null(NexusUpdateLogic.PickRecommended([]));
    }

    [Fact]
    public void SafeFileNameStripsPathCharacters()
    {
        Assert.Equal(".._.._name.zip", NexusUpdateLogic.SafeFileName("../..\\name.zip"));
        Assert.Equal("nexus-download", NexusUpdateLogic.SafeFileName(""));
    }

    [Fact]
    public void ParseFilesToleratesRealWorldV1Payload()
    {
        // 真实 v1 files.json:uploaded_time 是字符串日期、分类叫 cat_id、size 单位 KB。
        const string json = """
            {"files":[{
                "file_id": 248393,
                "is_primary": true,
                "size": 28293,
                "file_name": "Unbreakable mantles 127234 1.0.1 2025-02-03T04-19-44 7sK2xQ.7z",
                "version": "1.0.1",
                "cat_id": 1,
                "uploaded_time": "3 Feb 2025, 4:19AM"
            }]}
            """;
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var files = NexusApiClient.ParseFiles(doc);

        var file = Assert.Single(files);
        Assert.Equal(248393, file.FileId);
        Assert.Equal("1.0.1", file.Version);
        Assert.Equal(28293 * 1024, file.SizeBytes);
        Assert.Equal(1, file.CategoryId);
        Assert.True(file.IsPrimary);
        Assert.True(file.UploadedAt.Year == 2025 && file.UploadedAt.Month == 2, $"解析到 {file.UploadedAt}");
    }

    [Fact]
    public async Task CheckAggregatesAndCachesFileList()
    {
        var mod = ImportMod(version: "1.0");
        mod.NexusId = 530;
        var api = new FakeApi
        {
            Files = [MakeFile(id: 7, version: "2.0", uploaded: DateTimeOffset.Now)]
        };
        var nexus = new NexusUpdateService(_settings, _service, api);

        var info = await nexus.CheckAsync(_game, mod, force: false);

        Assert.True(info.Available);
        Assert.Equal("2.0", info.LatestVersion);
        Assert.Single(info.Files);

        // TTL 内二次检查命中缓存,不重复请求
        await nexus.CheckAsync(_game, mod, force: false);
        Assert.Equal(1, api.FileCalls);

        await nexus.CheckAsync(_game, mod, force: true);
        Assert.Equal(2, api.FileCalls);
    }

    [Fact]
    public void RequireApiKeyFailsWithoutKey()
    {
        _settings.Current.NexusApiKey = "";
        var nexus = new NexusUpdateService(_settings, _service, new FakeApi());

        Assert.Throws<InvalidOperationException>(() => nexus.RequireApiKey());
    }

    [Fact]
    public async Task DownloadAndUpdateDownloadsOnceAndAppliesUpdate()
    {
        var mod = ImportMod(version: "1.0");
        mod.NexusId = 530;
        var archive = MakeUpdateZip("updated-content");
        var api = new FakeApi
        {
            Files = [MakeFile(id: 99, version: "2.0", uploaded: DateTimeOffset.Now, name: "Update 530 2.0 2026-01-01T00-00-00 abc123.7z")],
            DownloadSource = archive
        };
        var nexus = new NexusUpdateService(_settings, _service, api);

        _service.SetEnabled(_game, mod, true);
        var batch = await nexus.DownloadAndUpdateAsync(_game, mod, 99);
        TrackDownloads();

        Assert.Same(mod, batch.Mods[0]);
        var deployed = Path.Combine(_root, "nativePC", "hello.bin");
        Assert.Equal("updated-content", File.ReadAllText(deployed));
        Assert.Equal(1, api.DownloadCalls);

        // 文件已在 downloads/:重试(如输密码)不重复下载
        await nexus.DownloadAndUpdateAsync(_game, mod, 99);
        Assert.Equal(1, api.DownloadCalls);
    }

    [Fact]
    public async Task DownloadSurfacesPremiumError()
    {
        var mod = ImportMod(version: "1.0");
        mod.NexusId = 530;
        var api = new FakeApi
        {
            Files = [MakeFile(id: 99, version: "2.0", uploaded: DateTimeOffset.Now)],
            LinkError = new InvalidOperationException("该操作需要 Nexus Premium 账户（API 生成下载链接仅限 Premium）")
        };
        var nexus = new NexusUpdateService(_settings, _service, api);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => nexus.DownloadAndUpdateAsync(_game, mod, 99));
        Assert.Contains("Premium", ex.Message);
    }

    [Fact]
    public async Task CheckRejectsModWithoutNexusSource()
    {
        var mod = ImportMod(version: "1.0");
        var nexus = new NexusUpdateService(_settings, _service, new FakeApi());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => nexus.CheckAsync(_game, mod, force: false));
    }

    private ModRecord ImportMod(string version)
    {
        var dir = Path.Combine(_root, "mod-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "nativePC", "hello.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "original");
        var mod = _service.Import(_game, dir).Mods[0];
        mod.Version = version;
        return mod;
    }

    private string MakeUpdateZip(string content)
    {
        var path = Path.Combine(_root, $"update-{Guid.NewGuid():N}.zip");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        using var entry = zip.CreateEntry("nativePC/hello.bin").Open();
        using var writer = new StreamWriter(entry);
        writer.Write(content);
        return path;
    }

    private static NexusModFile MakeFile(long id = 1, string version = "1.0", DateTimeOffset? uploaded = null,
        int category = 1, string name = "Main.7z") =>
        new(id, name, version, 1024, uploaded ?? DateTimeOffset.Now, category, IsPrimary: false);

    private void TrackDownloads()
    {
        var dir = AppPaths.DownloadDir;
        if (Directory.Exists(dir))
        {
            _downloads.AddRange(Directory.GetFiles(dir));
        }
    }

    public void Dispose()
    {
        _settings.Current.GamePaths.Remove(_appId);
        _settings.Current.NexusApiKey = "";
        _settings.Save();
        if (Directory.Exists(AppPaths.GameDir(_appId)))
        {
            Directory.Delete(AppPaths.GameDir(_appId), true);
        }

        foreach (var file in _downloads)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch
            {
            }
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private sealed class FakeApi : INexusApi
    {
        public List<NexusModFile> Files = [];
        public string DownloadSource = "";
        public Exception? LinkError;
        public int FileCalls;
        public int DownloadCalls;

        public Task<NexusUserInfo> ValidateAsync(string apiKey, CancellationToken cancellation = default) =>
            Task.FromResult(new NexusUserInfo(1, "tester", IsPremium: true, IsSupporter: true));

        public Task<NexusCheckResult> GetFilesAsync(string apiKey, string gameDomain, int modId, CancellationToken cancellation = default)
        {
            FileCalls++;
            return Task.FromResult(new NexusCheckResult(Files, 2500));
        }

        public Task<string> GetDownloadLinkAsync(string apiKey, string gameDomain, int modId, long fileId, CancellationToken cancellation = default)
        {
            if (LinkError is not null)
            {
                throw LinkError;
            }

            return Task.FromResult("https://cdn.example/file");
        }

        public Task DownloadToFileAsync(string url, string destPath, CancellationToken cancellation = default)
        {
            DownloadCalls++;
            File.Copy(DownloadSource, destPath, true);
            return Task.CompletedTask;
        }
    }
}
