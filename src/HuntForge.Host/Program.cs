using System.Text.Json.Serialization;
using HuntForge.Core;
using HuntForge.Host;
using HuntForge.Models;

var dataRoot = ResolveDataRoot(args);
AppPaths.Configure(dataRoot);
AppPaths.EnsureCreated();
Console.WriteLine($"HuntForge data: {AppPaths.Root}");

var settings = new SettingsStore();
settings.Load();
var service = new ModService(settings);
var gate = new object();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:17865");
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.AccessControlAllowOrigin = "*";
    context.Response.Headers.AccessControlAllowHeaders = "*";
    context.Response.Headers.AccessControlAllowMethods = "GET,POST,PUT,PATCH,DELETE,OPTIONS";
    if (HttpMethods.IsOptions(context.Request.Method))
    {
        context.Response.StatusCode = 204;
        return;
    }

    await next();
});

app.MapGet("/api/bootstrap", () =>
{
    lock (gate)
    {
        return Results.Json(ApiMapper.Bootstrap(service, settings));
    }
});

app.MapGet("/api/workspace/{gameId}", (GameId gameId) =>
{
    lock (gate)
    {
        return Results.Json(ApiMapper.Workspace(service, settings, gameId));
    }
});

app.MapPost("/api/games/{gameId}/select", (GameId gameId) =>
{
    lock (gate)
    {
        settings.Current.LastGame = gameId;
        settings.Save();
        return Results.Json(ApiMapper.Workspace(service, settings, gameId));
    }
});

app.MapPut("/api/games/{gameId}/path", (GameId gameId, PathRequest body) =>
{
    lock (gate)
    {
        var game = GameProfile.Get(gameId);
        settings.SetGamePath(game, body.Path);
        return Results.Json(ApiMapper.Workspace(service, settings, gameId));
    }
});

app.MapPut("/api/settings", (SettingsRequest body) =>
{
    lock (gate)
    {
        settings.Current.CheckGameRunning = body.CheckGameRunning;
        settings.Current.FixPakNumber = body.FixPakNumber;
        settings.Current.InstallOption = body.InstallOption;
        settings.Save();
        return Results.Json(ApiMapper.Bootstrap(service, settings));
    }
});

app.MapPost("/api/games/{gameId}/refresh", (GameId gameId) =>
{
    lock (gate)
    {
        service.Invalidate(GameProfile.Get(gameId));
        return Results.Json(ApiMapper.Workspace(service, settings, gameId));
    }
});

app.MapPost("/api/games/{gameId}/import", (GameId gameId, ImportRequest body) =>
{
    lock (gate)
    {
        var game = GameProfile.Get(gameId);
        var imported = 0;
        string? lastName = null;
        var failures = new List<string>();
        foreach (var path in ExpandImportPaths(body.Paths ?? []))
        {
            var label = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            try
            {
                var batch = service.Import(game, path);
                imported += batch.Mods.Count;
                lastName = batch.Group?.Name ?? batch.Mods.LastOrDefault()?.DisplayName;
            }
            catch (Exception ex)
            {
                failures.Add($"{label}: {ex.Message}");
            }
        }

        var workspace = ApiMapper.Workspace(service, settings, gameId);
        workspace.Status = failures.Count == 0
            ? imported == 1 && lastName is not null ? $"已导入 {lastName}" : $"已导入 {imported} 个 MOD"
            : $"已导入 {imported} 个，失败 {failures.Count} 个。{failures.FirstOrDefault()}";
        return Results.Json(workspace);
    }
});

app.MapPost("/api/games/{gameId}/mods/{modId}/enable", (GameId gameId, int modId, EnableRequest body) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        service.SetEnabled(game, RequireMod(service, game, modId), body.Enabled);
        return body.Enabled ? $"已启用 MOD" : "已禁用 MOD";
    }));

app.MapPost("/api/games/{gameId}/mods/{modId}/move", (GameId gameId, int modId, DeltaRequest body) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        service.Move(game, RequireMod(service, game, modId), body.Delta);
        return "MOD 优先级已更新";
    }));

app.MapPatch("/api/games/{gameId}/mods/{modId}", (GameId gameId, int modId, NameRequest body) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        service.RenameMod(game, RequireMod(service, game, modId), body.Name ?? "");
        return "MOD 已重命名";
    }));

app.MapPost("/api/games/{gameId}/mods/{modId}/update", (GameId gameId, int modId, ImportRequest body) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        var path = (body.Paths ?? []).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("请选择要更新的压缩包");
        }

        var batch = service.Update(game, RequireMod(service, game, modId), path);
        return batch.Group is not null
            ? $"已用合集替换，导入 {batch.Mods.Count} 个 MOD 到 {batch.Group.Name}"
            : $"已更新 {batch.Mods[0].DisplayName}";
    }));

app.MapPost("/api/games/{gameId}/mods/{modId}/group", (GameId gameId, int modId, GroupIdRequest body) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        service.MoveModToGroup(game, RequireMod(service, game, modId), body.GroupId);
        return "已移动 MOD";
    }));

app.MapDelete("/api/games/{gameId}/mods/{modId}", (GameId gameId, int modId) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        var mod = RequireMod(service, game, modId);
        var name = mod.DisplayName;
        service.Uninstall(game, mod);
        return $"已卸载 {name}";
    }));

app.MapPost("/api/games/{gameId}/groups", (GameId gameId, NameRequest body) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        var group = service.CreateGroup(game, string.IsNullOrWhiteSpace(body.Name) ? "新分组" : body.Name);
        return $"已创建 {group.Name}";
    }));

app.MapPatch("/api/games/{gameId}/groups/{groupId}", (GameId gameId, int groupId, GroupPatch body) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        var group = RequireGroup(service, game, groupId);
        if (body.Name is not null)
        {
            service.RenameGroup(game, group, body.Name);
        }

        if (body.Collapsed is bool collapsed)
        {
            service.SetGroupCollapsed(game, group, collapsed);
        }

        return "分组已更新";
    }));

app.MapPost("/api/games/{gameId}/groups/{groupId}/enable", (GameId gameId, int groupId, EnableRequest body) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        service.SetGroupEnabled(game, RequireGroup(service, game, groupId), body.Enabled);
        return body.Enabled ? "已启用分组" : "已禁用分组";
    }));

app.MapPost("/api/games/{gameId}/groups/{groupId}/move", (GameId gameId, int groupId, DeltaRequest body) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        service.MoveGroup(game, RequireGroup(service, game, groupId), body.Delta);
        return "分组顺序已更新";
    }));

app.MapPost("/api/games/{gameId}/groups/{groupId}/mods", (GameId gameId, int groupId, ModIdsRequest body) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        var mods = (body.ModIds ?? [])
            .Select(id => RequireMod(service, game, id))
            .ToList();
        service.MoveModsToGroup(game, mods, groupId);
        return $"已将 {mods.Count} 个 MOD 移到目标分组";
    }));

app.MapDelete("/api/games/{gameId}/groups/{groupId}", (GameId gameId, int groupId) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        var group = RequireGroup(service, game, groupId);
        var name = group.Name;
        service.DeleteGroup(game, group);
        return $"已删除分组 {name}";
    }));

app.MapPost("/api/games/{gameId}/clean", (GameId gameId) =>
    Mutate(gameId, service, settings, gate, game =>
    {
        service.CleanDeployed(game, false);
        return "已清理当前启用的 MOD 文件";
    }));

app.MapPost("/api/games/{gameId}/launch", (GameId gameId) =>
{
    lock (gate)
    {
        service.Launch(GameProfile.Get(gameId));
        return Results.Json(new { status = "已通过 Steam 启动游戏" });
    }
});

app.MapGet("/api/games/{gameId}/mods/{modId}/preview", (GameId gameId, int modId) =>
{
    lock (gate)
    {
        var game = GameProfile.Get(gameId);
        var mod = RequireMod(service, game, modId);
        var path = service.PreviewPath(game, mod);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return Results.NotFound();
        }

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };
        return Results.File(path, contentType);
    }
});

app.MapGet("/api/games/{gameId}/mods/{modId}/folder", (GameId gameId, int modId) =>
{
    lock (gate)
    {
        var game = GameProfile.Get(gameId);
        var mod = RequireMod(service, game, modId);
        var path = AppPaths.ModFilesDir(game.SteamAppId, mod.Id);
        return Directory.Exists(path)
            ? Results.Json(new { path })
            : Results.NotFound();
    }
});

app.Run();

static IResult Mutate(GameId gameId, ModService service, SettingsStore settings, object gate, Func<GameProfile, string> action)
{
    lock (gate)
    {
        var game = GameProfile.Get(gameId);
        try
        {
            var status = action(game);
            var workspace = ApiMapper.Workspace(service, settings, gameId);
            workspace.Status = status;
            return Results.Json(workspace);
        }
        catch (Exception ex)
        {
            var workspace = ApiMapper.Workspace(service, settings, gameId);
            workspace.Status = ex.Message;
            workspace.Error = true;
            return Results.Json(workspace);
        }
    }
}

static ModRecord RequireMod(ModService service, GameProfile game, int modId) =>
    service.GetMods(game).FirstOrDefault(mod => mod.Id == modId)
    ?? throw new InvalidOperationException("找不到这个 MOD");

static ModGroup RequireGroup(ModService service, GameProfile game, int groupId) =>
    service.GetGroups(game).FirstOrDefault(group => group.Id == groupId)
    ?? throw new InvalidOperationException("找不到这个分组");

static List<string> ExpandImportPaths(IEnumerable<string> paths)
{
    var files = new List<string>();
    foreach (var path in paths.Where(item => !string.IsNullOrWhiteSpace(item)))
    {
        if (File.Exists(path))
        {
            files.Add(path);
            continue;
        }

        if (Directory.Exists(path))
        {
            files.Add(path);
        }
    }

    return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

static string ResolveDataRoot(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] is "--data" or "--data-dir")
        {
            return Path.GetFullPath(args[i + 1]);
        }
    }

    var env = Environment.GetEnvironmentVariable("HUNTFORGE_DATA");
    if (!string.IsNullOrWhiteSpace(env))
    {
        return Path.GetFullPath(env);
    }

    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var current = new DirectoryInfo(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MHSeries.ModManager.slnx")))
            {
                return Path.Combine(current.FullName, "data");
            }

            current = current.Parent;
        }
    }

    return Path.Combine(AppContext.BaseDirectory, "data");
}

internal sealed record PathRequest(string? Path);
internal sealed record SettingsRequest(bool CheckGameRunning, bool FixPakNumber, int InstallOption);
internal sealed record ImportRequest(List<string>? Paths);
internal sealed record EnableRequest(bool Enabled);
internal sealed record DeltaRequest(int Delta);
internal sealed record GroupIdRequest(int GroupId);
internal sealed record NameRequest(string? Name);
internal sealed record GroupPatch(string? Name, bool? Collapsed);
internal sealed record ModIdsRequest(List<int>? ModIds);
