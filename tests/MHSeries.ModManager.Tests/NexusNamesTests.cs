using MhModManager.Core;
using MhModManager.Models;
using Xunit;

namespace MhModManager.Tests;

public sealed class NexusNamesTests
{
    [Theory]
    [InlineData("Dreamspell Magic Staff 4874 4 2026-09-16T11-43Z 8nis61VXS", "Dreamspell Magic Staff", 4874, "4")]
    [InlineData("Nasty Tome 2 4874 1.5.3 2026-01-02T03-04Z ab12cdEF", "Nasty Tome 2", 4874, "1.5.3")]
    [InlineData("Simple 1 1 2025-12-31T23-59Z A1b2C3d4", "Simple", 1, "1")]
    public void DownloadFilenamesAreParsedIntoNameAndId(string stem, string name, int id, string version)
    {
        var info = NexusNames.ParseNexusDownloadStem(stem);

        Assert.NotNull(info);
        Assert.Equal(name, info!.Value.Name);
        Assert.Equal(id, info.Value.ModId);
        Assert.Equal(version, info.Value.Version);
    }

    [Theory]
    [InlineData("Only Name And Id 4874 4")] // 缺日期与密钥
    [InlineData("Bad Date 4874 4 2026/09/16 8nis61VXS")] // 日期格式不对
    [InlineData("Bad Key 4874 4 2026-09-16T11-43Z key!")] // 密钥含符号
    [InlineData("Bad Id name version 2026-09-16T11-43Z 8nis61VXS")] // id 不是数字
    [InlineData("4874 4 2026-09-16T11-43Z 8nis61VXS")] // 没有名称部分
    public void NonDownloadFilenamesAreRejected(string stem)
    {
        Assert.Null(NexusNames.ParseNexusDownloadStem(stem));
    }

    [Fact]
    public void ApplyPrefersModinfoNameButStillTakesId()
    {
        var mod = new ParsedMod
        {
            Name = "模块配置里的名称",
            SourceFile = "Dreamspell Magic Staff 4874 4 2026-09-16T11-43Z 8nis61VXS.zip"
        };
        NexusNames.Apply(mod, mod.SourceFile);

        Assert.Equal("模块配置里的名称", mod.Name);
        Assert.Equal(4874, mod.NexusId);
        Assert.Equal("4", mod.Version);
    }

    [Fact]
    public void ApplyFallsBackToFilenameNameWhenModinfoNameMissing()
    {
        // 非 ModuleConfig 路径会先用原始文件名预填 Name，应被提取出的名称替换。
        const string file = "Dreamspell Magic Staff 4874 4 2026-09-16T11-43Z 8nis61VXS.zip";
        var mod = new ParsedMod { Name = Path.GetFileNameWithoutExtension(file), SourceFile = file };
        NexusNames.Apply(mod, file);

        Assert.Equal("Dreamspell Magic Staff", mod.Name);
        Assert.Equal(4874, mod.NexusId);
    }

    [Theory]
    [InlineData(GameId.World, "monsterhunterworld")]
    [InlineData(GameId.Rise, "monsterhunterrise")]
    [InlineData(GameId.Wilds, "monsterhunterwilds")]
    public void NexusUrlsUseTheGameSlug(GameId gameId, string slug)
    {
        Assert.Equal($"https://www.nexusmods.com/{slug}/mods/256", NexusNames.GetNexusUrl(GameProfile.Get(gameId), 256));
    }
}
