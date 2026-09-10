using MhModManager.Models;

namespace MhModManager.Core;

public sealed class SettingsStore
{
    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        AppPaths.EnsureCreated();
        Current = JsonUtil.Load(AppPaths.SettingsFile, new AppSettings());
    }

    public void Save() => JsonUtil.Save(AppPaths.SettingsFile, Current);

    public string? GetGamePath(GameProfile game) =>
        Current.GamePaths.TryGetValue(game.SteamAppId, out var path) ? path : null;

    public void SetGamePath(GameProfile game, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Current.GamePaths.Remove(game.SteamAppId);
        }
        else
        {
            Current.GamePaths[game.SteamAppId] = path;
        }

        Save();
    }
}
