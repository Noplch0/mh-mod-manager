using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace MhModManager.Core;

public sealed record NexusUserInfo(long UserId, string Name, bool IsPremium, bool IsSupporter);

public sealed record NexusModFile(
    long FileId,
    string FileName,
    string Version,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    int CategoryId,
    bool IsPrimary);

public sealed record NexusCheckResult(IReadOnlyList<NexusModFile> Files, int? RemainingRequests);

/// <summary>Nexus Mods 官方 REST API(v1)客户端。apikey 头 + 描述性 User-Agent 为官方强制要求。</summary>
public interface INexusApi
{
    Task<NexusUserInfo> ValidateAsync(string apiKey, CancellationToken cancellation = default);

    Task<NexusCheckResult> GetFilesAsync(string apiKey, string gameDomain, int modId, CancellationToken cancellation = default);

    Task<string> GetDownloadLinkAsync(string apiKey, string gameDomain, int modId, long fileId, CancellationToken cancellation = default);

    Task DownloadToFileAsync(string url, string destPath, CancellationToken cancellation = default);
}

public sealed class NexusApiClient : INexusApi
{
    private const string BaseUrl = "https://api.nexusmods.com/v1";
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("mh-mod-manager", "1.4"));
        return client;
    }

    public async Task<NexusUserInfo> ValidateAsync(string apiKey, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(apiKey, "users/validate.json", cancellation);
        using var doc = await ReadDocumentAsync(response, cancellation);
        var root = doc?.RootElement ?? default;
        return new NexusUserInfo(
            ReadLong(root, "user_id"),
            ReadString(root, "name"),
            ReadBool(root, "is_premium"),
            ReadBool(root, "is_supporter"));
    }

    public async Task<NexusCheckResult> GetFilesAsync(string apiKey, string gameDomain, int modId, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(apiKey, $"games/{gameDomain}/mods/{modId}/files.json", cancellation);
        using var doc = await ReadDocumentAsync(response, cancellation);
        return new NexusCheckResult(ParseFiles(doc), ReadRemaining(response));
    }

    /// <summary>v1 files.json 实际字段与文档有出入:uploaded_time 是字符串日期、分类叫 cat_id、size 单位是 KB,这里全部容错读取。</summary>
    internal static IReadOnlyList<NexusModFile> ParseFiles(JsonDocument? document)
    {
        if (document is null ||
            !document.RootElement.TryGetProperty("files", out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray().Select(entry => new NexusModFile(
            ReadLong(entry, "file_id"),
            ReadString(entry, "file_name"),
            ReadString(entry, "version"),
            ReadSizeBytes(entry),
            ReadTimestamp(entry),
            (int)ReadLong(entry, "cat_id", "category_id"),
            ReadBool(entry, "is_primary"))).ToList();
    }

    public async Task<string> GetDownloadLinkAsync(string apiKey, string gameDomain, int modId, long fileId, CancellationToken cancellation = default)
    {
        using var response = await SendAsync(apiKey, $"games/{gameDomain}/mods/{modId}/files/{fileId}/download_link.json", cancellation);
        using var doc = await ReadDocumentAsync(response, cancellation);
        if (doc?.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var uri = ReadString(item, "URI", "uri");
                if (!string.IsNullOrWhiteSpace(uri))
                {
                    return uri;
                }
            }
        }

        throw new InvalidOperationException("Nexus 未返回下载链接");
    }

    public async Task DownloadToFileAsync(string url, string destPath, CancellationToken cancellation = default)
    {
        // 下载大文件必须流式写盘;Client.Timeout 对流式读取不生效,单独用 CTS 控制。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"下载失败 ({(int)response.StatusCode})");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        var temp = destPath + ".part";
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
            await using var target = File.Create(temp);
            await source.CopyToAsync(target, 81920, timeout.Token);
        }
        finally
        {
            if (timeout.Token.IsCancellationRequested && File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                }
            }
        }

        File.Move(temp, destPath, true);
    }

    private static async Task<HttpResponseMessage> SendAsync(string apiKey, string path, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/{path}");
        request.Headers.Add("apikey", apiKey);
        var response = await Client.SendAsync(request, cancellation);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var message = (int)response.StatusCode switch
        {
            401 => "Nexus API Key 无效，请在设置中检查",
            403 => "该操作需要 Nexus Premium 账户（API 生成下载链接仅限 Premium）",
            404 => "Nexus 上找不到这个 MOD 或文件",
            429 => "Nexus API 请求过于频繁，请稍后再试",
            _ => $"Nexus API 请求失败 ({(int)response.StatusCode})"
        };
        throw new InvalidOperationException(message);
    }

    private static async Task<JsonDocument?> ReadDocumentAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellation);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellation);
    }

    private static int? ReadRemaining(HttpResponseMessage response)
    {
        var header = response.Headers.FirstOrDefault(item =>
            item.Key.Equals("X-RL-Daily-Remaining", StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();
        return int.TryParse(header, out var remaining) ? remaining : null;
    }

    internal static long ReadLong(JsonElement entry, params string[] names)
    {
        foreach (var name in names)
        {
            if (entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt64(out var number))
            {
                return number;
            }
        }

        return 0;
    }

    internal static string ReadString(JsonElement entry, params string[] names)
    {
        foreach (var name in names)
        {
            if (entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }
        }

        return "";
    }

    private static bool ReadBool(JsonElement entry, params string[] names)
    {
        foreach (var name in names)
        {
            if (entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.True)
            {
                return true;
            }
        }

        return false;
    }

    private static long ReadSizeBytes(JsonElement entry)
    {
        if (ReadLong(entry, "size_in_bytes") is > 0 and var bytes)
        {
            return bytes;
        }

        // v1 的 size 字段单位是 KB。
        return ReadLong(entry, "size") * 1024;
    }

    private static DateTimeOffset ReadTimestamp(JsonElement entry)
    {
        foreach (var name in new[] { "uploaded_timestamp", "uploaded_time" })
        {
            if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unix))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(unix);
                }

                if (value.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    return parsed;
                }
            }
        }

        return DateTimeOffset.MinValue;
    }
}
