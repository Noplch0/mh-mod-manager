using System.Text.RegularExpressions;
using MhModManager.Models;
using Microsoft.Win32;

namespace MhModManager.Core;

public static class SteamLocator
{
    public static string? FindGamePath(GameProfile game)
    {
        var fromUninstall = ReadUninstallPath(game.SteamAppId);
        if (IsGameDir(fromUninstall, game.ExeName))
        {
            return fromUninstall;
        }

        foreach (var library in EnumerateLibraries())
        {
            var candidate = Path.Combine(library, "steamapps", "common", game.SteamFolder);
            if (IsGameDir(candidate, game.ExeName))
            {
                return candidate;
            }
        }

        return null;
    }

    public static IEnumerable<string> EnumerateLibraries()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in EnumerateSteamRoots())
        {
            if (seen.Add(root))
            {
                yield return root;
            }

            foreach (var vdf in new[]
                     {
                         Path.Combine(root, "steamapps", "libraryfolders.vdf"),
                         Path.Combine(root, "SteamApps", "libraryfolders.vdf")
                     })
            {
                if (!File.Exists(vdf))
                {
                    continue;
                }

                foreach (var path in ParseLibraryPaths(File.ReadAllText(vdf)))
                {
                    if (seen.Add(path))
                    {
                        yield return path;
                    }
                }
            }
        }
    }

    public static IEnumerable<string> EnumerateSteamRoots()
    {
        var values = new List<string?>
        {
            ReadHkcu(@"Software\Valve\Steam", "SteamPath"),
            ReadHklm(@"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            ReadHklm(@"SOFTWARE\Valve\Steam", "InstallPath")
        };

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var full = Path.GetFullPath(value.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(full))
            {
                yield return full;
            }
        }
    }

    public static IEnumerable<string> ParseLibraryPaths(string vdf)
    {
        foreach (Match match in Regex.Matches(vdf, "\"path\"\\s+\"([^\"]+)\""))
        {
            var path = match.Groups[1].Value.Replace(@"\\", @"\");
            if (Directory.Exists(path))
            {
                yield return Path.GetFullPath(path);
            }
        }
    }

    public static bool IsGameDir(string? dir, string exeName) =>
        !string.IsNullOrWhiteSpace(dir)
        && Directory.Exists(dir)
        && File.Exists(Path.Combine(dir, exeName));

    private static string? ReadUninstallPath(int appId)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {appId}");
            return key?.GetValue("InstallLocation") as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadHkcu(string path, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadHklm(string path, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }
}
