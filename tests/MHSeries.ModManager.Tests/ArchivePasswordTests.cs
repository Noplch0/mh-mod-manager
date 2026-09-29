using System.IO.Compression;
using MhModManager.Core;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

/// <summary>加密压缩包:解压密码流程、复制/移动模式下存储源去密码、原位模式不动用户文件。</summary>
public sealed class ArchivePasswordTests : IDisposable
{
    private const string Password = "mhmod123";
    private const string Content = "secret-mod-content";

    private readonly int _appId = Random.Shared.Next(9_000_001, 9_900_000);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mh-mod-manager-service-tests", Guid.NewGuid().ToString("N"));
    private readonly SettingsStore _settings = new();
    private readonly GameProfile _game;
    private readonly ModService _service;
    private readonly string _workDir;
    private readonly List<string> _downloads = [];

    public ArchivePasswordTests()
    {
        _game = new GameProfile(GameId.World, "test", "test", "test", "test.exe", _appId, "test", "", 0);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, _game.ExeName), "fake-game");
        _settings.Load();
        _settings.Current.CheckGameRunning = false;
        _settings.SetGamePath(_game, _root);
        _service = new ModService(_settings);
        _workDir = Path.Combine(_root, "imports");
        Directory.CreateDirectory(_workDir);
    }

    [Theory]
    [InlineData("encrypted-zipcrypto.zip")]
    [InlineData("encrypted-aes.zip")]
    [InlineData("encrypted.7z")]
    public void ExtractWithoutPasswordThrowsPasswordRequired(string fixture)
    {
        var archive = CopyFixture(fixture);
        var dest = Path.Combine(_root, "extract-" + Guid.NewGuid().ToString("N"));

        Assert.Throws<PasswordRequiredException>(() => ArchiveExtractor.Extract(archive, dest));
    }

    [Theory]
    [InlineData("encrypted-zipcrypto.zip")]
    [InlineData("encrypted-aes.zip")]
    [InlineData("encrypted.7z")]
    public void ExtractWithCorrectPasswordSucceeds(string fixture)
    {
        var archive = CopyFixture(fixture);
        var dest = Path.Combine(_root, "extract-" + Guid.NewGuid().ToString("N"));

        ArchiveExtractor.Extract(archive, dest, null, [Password]);

        Assert.Equal(Content, File.ReadAllText(Path.Combine(dest, "nativePC", "hello.bin")));
    }

    [Fact]
    public void ExtractWithOnlyWrongPasswordThrowsPasswordRequired()
    {
        var archive = CopyFixture("encrypted-zipcrypto.zip");
        var dest = Path.Combine(_root, "extract-" + Guid.NewGuid().ToString("N"));

        var ex = Assert.Throws<PasswordRequiredException>(() => ArchiveExtractor.Extract(archive, dest, null, ["wrong"]));

        Assert.StartsWith("密码不正确", ex.Message);
    }

    [Fact]
    public void CopyOptionImportStripsPasswordFromStoredSource()
    {
        _settings.Current.InstallOption = 1;
        var source = CopyFixture("encrypted-zipcrypto.zip", $"enc-{Guid.NewGuid():N}.zip");

        var installed = _service.Import(_game, source, null, [Password]).Mods[0];
        TrackDownload(installed.SourceFile);

        Assert.Equal(AppPaths.DownloadDir, Path.GetDirectoryName(installed.SourceFile));
        Assert.Equal(".zip", Path.GetExtension(installed.SourceFile));
        Assert.False(ArchiveExtractor.IsEncrypted(installed.SourceFile));
        Assert.True(File.Exists(source));
        Assert.True(ArchiveExtractor.IsEncrypted(source));

        var verify = Path.Combine(_root, "verify-" + Guid.NewGuid().ToString("N"));
        ArchiveExtractor.Extract(installed.SourceFile, verify);
        Assert.Equal(Content, File.ReadAllText(Path.Combine(verify, "nativePC", "hello.bin")));
    }

    [Fact]
    public void MoveOptionImportStripsPasswordFromStoredSource()
    {
        _settings.Current.InstallOption = 2;
        var source = CopyFixture("encrypted.7z", $"mov-{Guid.NewGuid():N}.7z");

        var installed = _service.Import(_game, source, null, [Password]).Mods[0];
        TrackDownload(installed.SourceFile);

        Assert.Equal(".zip", Path.GetExtension(installed.SourceFile));
        Assert.False(ArchiveExtractor.IsEncrypted(installed.SourceFile));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void KeepInPlaceImportDoesNotTouchEncryptedSource()
    {
        _settings.Current.InstallOption = 0;
        var source = CopyFixture("encrypted.7z", $"ins-{Guid.NewGuid():N}.7z");

        var installed = _service.Import(_game, source, null, [Password]).Mods[0];

        Assert.Equal(source, installed.SourceFile);
        Assert.True(ArchiveExtractor.IsEncrypted(source));
    }

    [Fact]
    public void CleanArchiveWithPasswordsProvidedKeepsStoredBytes()
    {
        _settings.Current.InstallOption = 1;
        var stem = $"plain-{Guid.NewGuid():N}";
        var source = CreatePlainZip(stem);
        var bytes = File.ReadAllBytes(source);

        var installed = _service.Import(_game, source, null, [Password]).Mods[0];
        TrackDownload(installed.SourceFile);

        Assert.Equal(Path.Combine(AppPaths.DownloadDir, stem + ".zip"), installed.SourceFile);
        Assert.Equal(bytes, File.ReadAllBytes(installed.SourceFile));
    }

    [Fact]
    public void WrongPasswordImportLeavesNoModBehind()
    {
        _settings.Current.InstallOption = 1;
        var source = CopyFixture("encrypted-aes.zip", $"fail-{Guid.NewGuid():N}.zip");

        Assert.Throws<PasswordRequiredException>(() => _service.Import(_game, source, null, ["wrong"]));

        Assert.Empty(_service.GetMods(_game));
    }

    [Fact]
    public void NestedEncryptedZipNeedsPasswordThenImportsStripped()
    {
        _settings.Current.InstallOption = 1;
        var innerEncrypted = CopyFixture("encrypted-zipcrypto.zip", "inner.zip");
        var outer = Path.Combine(_workDir, $"outer-{Guid.NewGuid():N}.zip");
        using (var stream = File.Create(outer))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(innerEncrypted, "inner.zip");
        }

        var denied = Assert.Throws<PasswordRequiredException>(() => _service.Import(_game, outer));
        Assert.Contains("inner.zip", denied.Message);

        var installed = _service.Import(_game, outer, null, [Password]).Mods[0];
        TrackDownload(installed.SourceFile);

        Assert.False(ArchiveExtractor.IsEncrypted(installed.SourceFile));
        var verify = Path.Combine(_root, "verify-" + Guid.NewGuid().ToString("N"));
        ArchiveExtractor.Extract(installed.SourceFile, verify);
        var innerPath = Path.Combine(verify, "inner.zip");
        Assert.True(File.Exists(innerPath));
        Assert.False(ArchiveExtractor.IsEncrypted(innerPath));

        var innerDest = Path.Combine(_root, "inner-" + Guid.NewGuid().ToString("N"));
        ArchiveExtractor.Extract(innerPath, innerDest);
        Assert.Equal(Content, File.ReadAllText(Path.Combine(innerDest, "nativePC", "hello.bin")));
    }

    [Fact]
    public void UpdateWithEncryptedPackageStripsStoredSource()
    {
        _settings.Current.InstallOption = 1;
        var installed = _service.Import(_game, CreatePlainZip($"base-{Guid.NewGuid():N}")).Mods[0];
        TrackDownload(installed.SourceFile);
        var encrypted = CopyFixture("encrypted-aes.zip", $"upd-{Guid.NewGuid():N}.zip");

        var updated = _service.Update(_game, installed, encrypted, null, [Password]).Mods[0];
        TrackDownload(updated.SourceFile);

        Assert.False(ArchiveExtractor.IsEncrypted(updated.SourceFile));
        Assert.True(File.Exists(encrypted));
        Assert.True(ArchiveExtractor.IsEncrypted(encrypted));
    }

    private string CopyFixture(string name, string? targetName = null)
    {
        var dest = Path.Combine(_workDir, targetName ?? name);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Testdata", name), dest, true);
        return dest;
    }

    private string CreatePlainZip(string stem)
    {
        var path = Path.Combine(_workDir, stem + ".zip");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        using var entry = zip.CreateEntry("nativePC/hello.bin").Open();
        using var writer = new StreamWriter(entry);
        writer.Write(Content);
        return path;
    }

    private void TrackDownload(string file)
    {
        _downloads.Add(file);
        _downloads.Add(file + ".bak");
    }

    public void Dispose()
    {
        _settings.Current.GamePaths.Remove(_appId);
        _settings.Current.InstallOption = 0;
        _settings.Save();
        if (Directory.Exists(AppPaths.GameDir(_appId)))
        {
            Directory.Delete(AppPaths.GameDir(_appId), true);
        }

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }

        foreach (var file in _downloads)
        {
            TryDeleteFile(file);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
