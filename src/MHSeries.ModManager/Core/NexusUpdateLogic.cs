namespace MhModManager.Core;

/// <summary>Nexus 更新判定的纯函数:推荐文件挑选、版本比较、文件名清洗。</summary>
public static class NexusUpdateLogic
{
    /// <summary>Nexus 文件分类:1=MAIN, 2=UPDATE, 3=OPTIONAL, 4=OLD, 6=MISC。</summary>
    public const int MainCategory = 1;

    /// <summary>推荐下载项:主分类(MAIN)或主文件优先,同组内取上传时间最新。</summary>
    public static NexusModFile? PickRecommended(IReadOnlyList<NexusModFile> files)
    {
        if (files.Count == 0)
        {
            return null;
        }

        var main = files.Where(IsMainFile).ToList();
        return (main.Count > 0 ? main : files)
            .OrderByDescending(file => file.UploadedAt)
            .First();
    }

    public static bool IsMainFile(NexusModFile file) =>
        file.CategoryId == MainCategory || file.IsPrimary;

    /// <summary>
    /// 是否有更新:版本号不同 → 有;版本相同但文件比安装时间新(重新上传)→ 有;
    /// 本地版本未知(空)→ 有文件即视为可能有更新,由用户判断。
    /// </summary>
    public static bool HasUpdate(string installedVersion, DateTimeOffset installedAt, NexusModFile file)
    {
        if (string.IsNullOrWhiteSpace(installedVersion))
        {
            return true;
        }

        var normalized = NormalizeVersion(installedVersion);
        if (!string.Equals(normalized, NormalizeVersion(file.Version), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return file.UploadedAt > installedAt;
    }

    /// <summary>版本归一化:剥 v/V 前缀,按点/斜杠分段,纯数字段补零到两位,非数字段保留原样小写。</summary>
    public static string NormalizeVersion(string version)
    {
        var trimmed = (version ?? "").Trim();
        if (trimmed.Length > 1 && trimmed[0] is 'v' or 'V' && char.IsAsciiDigit(trimmed[1]))
        {
            trimmed = trimmed[1..];
        }

        var parts = trimmed.Split('.', '/', '-', '_');
        return string.Join(".", parts.Select(part =>
            int.TryParse(part, out var number) ? number.ToString("D2") : part.ToLowerInvariant()));
    }

    /// <summary>下载文件名清洗:Nexus 文件名可能带路径或非法字符,统一为安全文件名。</summary>
    public static string SafeFileName(string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "nexus-download" : fileName.Trim();
        name = name.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}
