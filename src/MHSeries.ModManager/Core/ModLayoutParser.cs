using MhModManager.Models;

namespace MhModManager.Core;

public static class ModLayoutParser
{
    private static readonly string[] PreviewNames =
    [
        "preview.png", "preview.jpg", "preview.jpeg", "preview.webp",
        "screenshot.png", "screenshot.jpg", "screenshot.jpeg", "screenshot.webp",
        "cover.png", "cover.jpg", "cover.jpeg", "cover.webp",
        "未标题-1.jpg", "未标题-1.png"
    ];

    private static readonly string[] PreviewExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp"];

    private static readonly HashSet<string> GameDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        "dinput8.dll", "dxgi.dll", "version.dll", "akconvolutionreverb.dll", "aksoundseedair.dll",
        "amd_ags._x64.dll", "amd_fidelityfx_dx12.dll", "crashhandler.dll", "crashreportdll.dll",
        "dstorage.dll", "dstoragecore.dll", "libxess.dll", "masteringsuite.dll", "nvngx_dlss.dll",
        "nvngx_dlssg.dll", "partywin.dll", "sl.common.dll", "sl.dlss.dll", "sl.dlss_g.dll",
        "sl.interposer.dll", "sl.pcl.dll", "sl.reflex.dll", "steam_api64.dll"
    };

    public static ParsedMod Parse(GameProfile game, string sourceFile, string extractedDir)
    {
        var root = ArchiveExtractor.UnwrapRoot(extractedDir);
        var moduleConfig = Directory.GetFiles(root, "ModuleConfig.xml", SearchOption.AllDirectories).FirstOrDefault();
        if (moduleConfig is not null)
        {
            return ModuleConfigParser.Parse(game, sourceFile, extractedDir, moduleConfig);
        }

        var parsed = new ParsedMod
        {
            StagingDir = extractedDir,
            SourceFile = sourceFile,
            Name = Path.GetFileNameWithoutExtension(sourceFile)
        };

        ApplyIni(parsed, root);
        NexusNames.Apply(parsed, sourceFile);
        parsed.PreviewSource = FindPreview(root);

        if (string.IsNullOrWhiteSpace(parsed.Version))
        {
            parsed.Version = File.GetLastWriteTime(sourceFile).ToString("yyyy-MM-dd");
        }

        foreach (var item in EnumerateDeployFiles(game, root))
        {
            if (Path.GetFullPath(item.SourcePath).Equals(Path.GetFullPath(sourceFile), StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(parsed.PreviewSource) &&
                Path.GetFullPath(item.SourcePath).Equals(Path.GetFullPath(parsed.PreviewSource), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isUnmappedPak = game.UsesPakPatches && IsPakFile(item.RelativeDest);
            if (!IsSafeStoredPath(item.RelativeDest) ||
                (!isUnmappedPak && !IsSafeDeploymentPath(game, item.RelativeDest)))
            {
                throw new InvalidDataException($"MOD 包含不允许部署到游戏目录的文件: {item.RelativeDest}");
            }

            parsed.Files.Add(item);
        }

        parsed.Category = InferCategory(game, parsed);
        return parsed;
    }

    internal static string RemapDestination(GameProfile game, string relative)
    {
        var path = relative.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(path) || IsSafeDeploymentPath(game, path))
        {
            return path;
        }

        if (game.Id == GameId.World)
        {
            if (StartsWithAny(path, "pl/", "wp/", "plugins/") || EqualsAny(path, "pl", "wp", "plugins"))
            {
                return $"nativePC/{path}";
            }

            return path.Contains('/') ? $"nativePC/{path}" : path;
        }

        if (StartsWithAny(path, "weapon/", "player/", "art/", "gamedesign/") ||
            EqualsAny(path, "weapon", "player", "art", "gamedesign"))
        {
            return $"natives/{path}";
        }

        if (path.StartsWith("autorun/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("autorun", StringComparison.OrdinalIgnoreCase))
        {
            return path.StartsWith("reframework/", StringComparison.OrdinalIgnoreCase) ? path : $"reframework/{path}";
        }

        if (path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("plugins", StringComparison.OrdinalIgnoreCase))
        {
            return $"reframework/{path}";
        }

        if (path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) && !path.Contains('/'))
        {
            return $"reframework/autorun/{path}";
        }

        if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !path.Contains('/') &&
            !GameDlls.Contains(Path.GetFileName(path)))
        {
            return $"reframework/plugins/{path}";
        }

        return path;
    }

    private static bool StartsWithAny(string path, params string[] prefixes) =>
        prefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool EqualsAny(string path, params string[] names) =>
        names.Any(name => path.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static bool IsSafeDeploymentPath(GameProfile game, string relative)
    {
        var path = relative.Replace('\\', '/').TrimEnd('/');
        if (!IsSafeStoredPath(path))
        {
            return false;
        }

        if (game.Id == GameId.World)
        {
            return path.StartsWith("nativePC/", StringComparison.OrdinalIgnoreCase) || !path.Contains('/');
        }

        if (path.StartsWith("natives/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("reframework/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (path.StartsWith(game.PakPrefix, StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)
                && !path.Contains('/'))
               || !path.Contains('/');
    }

    public static bool IsSafeStoredPath(string relative)
    {
        var path = relative.Replace('\\', '/').TrimEnd('/');
        if (string.IsNullOrWhiteSpace(path) || path.Contains(':') || Path.IsPathRooted(path))
        {
            return false;
        }

        var parts = path.Split('/');
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part == "." || part == ".."))
        {
            return false;
        }

        return !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<ParsedFile> EnumerateDeployFiles(GameProfile game, string root)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ParsedFile>();

        void AddFile(string source, string destination)
        {
            if (!File.Exists(source) || ShouldSkipFile(source))
            {
                return;
            }

            var normalized = destination.Replace('\\', '/').Trim('/');
            if (seen.Add(normalized))
            {
                result.Add(new ParsedFile { SourcePath = source, RelativeDest = normalized });
            }
        }

        void AddDirectory(string source, string destinationPrefix)
        {
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                AddFile(file, CombineDest(destinationPrefix, relative));
            }
        }

        void AddTopLevelFiles(string destinationPrefix)
        {
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly))
            {
                if (!Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    AddFile(file, CombineDest(destinationPrefix, Path.GetFileName(file)));
                }
            }
        }

        bool AddWorldRootFiles()
        {
            var added = false;
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file);
                if (!Path.GetExtension(fileName).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    AddFile(file, fileName);
                    added = true;
                }
            }

            return added;
        }

        var world = game.Id == GameId.World;
        var modelDirs = world
            ? new[] { "pl", "wp", "plugins" }
            : game.Id == GameId.Rise
                ? new[] { "weapon", "player" }
                : new[] { "art", "gamedesign" };

        if (world)
        {
            var nativePc = Path.Combine(root, "nativePC");
            if (Directory.Exists(nativePc))
            {
                AddDirectory(nativePc, "nativePC");
                AddWorldRootFiles();
                return result;
            }

            var addedModel = false;
            foreach (var dir in modelDirs)
            {
                var source = Path.Combine(root, dir);
                if (Directory.Exists(source))
                {
                    AddDirectory(source, $"nativePC/{dir}");
                    addedModel = true;
                }
            }

            if (addedModel)
            {
                AddWorldRootFiles();
                return result;
            }

            if (modelDirs.Contains(Path.GetFileName(root), StringComparer.OrdinalIgnoreCase))
            {
                AddDirectory(root, $"nativePC/{Path.GetFileName(root)}");
                return result;
            }

            var nested = Directory.GetDirectories(root).FirstOrDefault(dir =>
                Directory.Exists(Path.Combine(dir, "nativePC")) ||
                modelDirs.Any(name => Directory.Exists(Path.Combine(dir, name))));
            if (nested is not null)
            {
                foreach (var item in EnumerateDeployFiles(game, nested))
                {
                    result.Add(item);
                }
                return result;
            }

            if (AddWorldRootFiles())
            {
                return result;
            }

            AddDirectory(root, "nativePC");
            return result;
        }

        var addedExplicit = false;
        var natives = Path.Combine(root, "natives");
        var reframework = Path.Combine(root, "reframework");
        if (Directory.Exists(natives))
        {
            AddDirectory(natives, "natives");
            addedExplicit = true;
        }

        if (Directory.Exists(reframework))
        {
            AddDirectory(reframework, "reframework");
            addedExplicit = true;
        }

        foreach (var dir in modelDirs)
        {
            var source = Path.Combine(root, dir);
            if (Directory.Exists(source))
            {
                AddDirectory(source, $"natives/{dir}");
                addedExplicit = true;
            }
        }

        if (modelDirs.Contains(Path.GetFileName(root), StringComparer.OrdinalIgnoreCase))
        {
            AddDirectory(root, $"natives/{Path.GetFileName(root)}");
            addedExplicit = true;
        }

        var autorun = Path.Combine(root, "autorun");
        var plugins = Path.Combine(root, "plugins");
        if (Directory.Exists(autorun))
        {
            AddDirectory(autorun, "reframework/autorun");
            addedExplicit = true;
        }

        if (Directory.Exists(plugins))
        {
            AddDirectory(plugins, "reframework/plugins");
            addedExplicit = true;
        }

        if (Path.GetFileName(root).Equals("autorun", StringComparison.OrdinalIgnoreCase))
        {
            AddDirectory(root, "reframework/autorun");
            addedExplicit = true;
        }

        if (Path.GetFileName(root).Equals("plugins", StringComparison.OrdinalIgnoreCase))
        {
            AddDirectory(root, "reframework/plugins");
            addedExplicit = true;
        }

        if (addedExplicit)
        {
            AddTopLevelFiles("");
            return result;
        }

        if (Directory.GetFiles(root, "*.pak", SearchOption.TopDirectoryOnly).Length > 0)
        {
            AddTopLevelFiles("");
            return result;
        }

        if (Directory.GetFiles(root, "*.lua", SearchOption.AllDirectories).Length > 0)
        {
            AddDirectory(root, "reframework/autorun");
            return result;
        }

        var dlls = Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories);
        if (dlls.Length > 0)
        {
            var rootPackage = dlls.Any(file => GameDlls.Contains(Path.GetFileName(file)));
            AddDirectory(root, rootPackage ? "" : "reframework/plugins");
            return result;
        }

        var nestedReRoot = Directory.GetDirectories(root).FirstOrDefault(dir =>
            Directory.Exists(Path.Combine(dir, "natives")) ||
            Directory.Exists(Path.Combine(dir, "reframework")) ||
            modelDirs.Any(name => Directory.Exists(Path.Combine(dir, name))));
        if (nestedReRoot is not null)
        {
            foreach (var item in EnumerateDeployFiles(game, nestedReRoot))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static bool ShouldSkipFile(string file)
    {
        var name = Path.GetFileName(file);
        return name.Equals("modinfo.ini", StringComparison.OrdinalIgnoreCase)
               || name.Equals("meta.ini", StringComparison.OrdinalIgnoreCase)
               || name.Equals("ModuleConfig.xml", StringComparison.OrdinalIgnoreCase);
    }

    public static string DetectPrefix(GameProfile game, string root)
    {
        var name = Path.GetFileName(root);
        if (name.Equals(game.SteamFolder, StringComparison.OrdinalIgnoreCase) ||
            name.Equals(game.SteamFolder + "Demo", StringComparison.OrdinalIgnoreCase) ||
            name.Equals(game.ExeName.Replace(".exe", ""), StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        if (game.Id == GameId.World)
        {
            if (DirExists(root, "nativePC"))
            {
                return "";
            }

            if (HasAnyDir(root, "pl", "wp", "plugins"))
            {
                return "nativePC";
            }

            return "nativePC";
        }

        if (DirExists(root, "natives") || DirExists(root, "reframework") || HasFile(root, "dinput8.dll"))
        {
            return "";
        }

        if (name.Equals("autorun", StringComparison.OrdinalIgnoreCase))
        {
            return "reframework/autorun";
        }

        if (name.Equals("plugins", StringComparison.OrdinalIgnoreCase))
        {
            return "reframework/plugins";
        }

        if (HasExtension(root, ".pak"))
        {
            return "";
        }

        if (HasExtension(root, ".lua"))
        {
            return "reframework/autorun";
        }

        if (HasExtension(root, ".dll"))
        {
            return HasGameDll(root) ? "" : "reframework/plugins";
        }

        if (game.Id == GameId.Rise && HasAnyDir(root, "weapon", "player"))
        {
            return "natives";
        }

        if (game.Id == GameId.Wilds && HasAnyDir(root, "art", "gamedesign"))
        {
            return "natives";
        }

        var nested = FindNestedRoot(game, root);
        if (nested is not null)
        {
            return Path.GetRelativePath(root, nested).Replace('\\', '/') switch
            {
                "." => DetectPrefix(game, nested),
                var rel => rel
            };
        }

        return game.Id == GameId.World ? "nativePC" : "";
    }

    public static bool IsPakFile(string relative)
    {
        var normalized = relative.Replace('\\', '/');
        return normalized.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)
               && !normalized.Contains('/');
    }

    private static string? FindNestedRoot(GameProfile game, string root)
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            var prefix = DetectPrefixShallow(game, dir);
            if (!string.IsNullOrEmpty(prefix) || LooksLikeDeployRoot(game, dir))
            {
                return dir;
            }
        }

        return null;
    }

    private static string DetectPrefixShallow(GameProfile game, string root)
    {
        if (game.Id == GameId.World && DirExists(root, "nativePC"))
        {
            return "";
        }

        if (game.UsesReFramework &&
            (DirExists(root, "natives") || DirExists(root, "reframework") || HasFile(root, "dinput8.dll")))
        {
            return "";
        }

        return "";
    }

    private static bool LooksLikeDeployRoot(GameProfile game, string dir)
    {
        if (game.Id == GameId.World)
        {
            return DirExists(dir, "nativePC") || HasAnyDir(dir, "pl", "wp", "plugins");
        }

        return DirExists(dir, "natives") || DirExists(dir, "reframework") || HasExtension(dir, ".pak");
    }

    private static void ApplyIni(ParsedMod parsed, string root)
    {
        foreach (var name in new[] { "modinfo.ini", "meta.ini" })
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path))
            {
                var found = Directory.GetFiles(root, name, SearchOption.AllDirectories).FirstOrDefault();
                path = found ?? "";
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                continue;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                var split = line.IndexOf('=');
                if (split < 0)
                {
                    continue;
                }

                var key = line[..split].Trim().ToLowerInvariant();
                var value = line[(split + 1)..].Trim();
                switch (key)
                {
                    case "name" when !string.IsNullOrWhiteSpace(value):
                        parsed.Name = value;
                        break;
                    case "version":
                        parsed.Version = value;
                        break;
                    case "author":
                        parsed.Author = value;
                        break;
                    case "description":
                        parsed.Description = value.Replace("\\n", "\n");
                        break;
                    case "nameasbundle":
                        parsed.BundleName = value;
                        break;
                }
            }

            break;
        }
    }

    private static string FindPreview(string root)
    {
        foreach (var name in PreviewNames)
        {
            var direct = Path.Combine(root, name);
            if (File.Exists(direct))
            {
                return direct;
            }
        }

        var images = Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Where(file => PreviewExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (images.Count == 1)
        {
            return images[0];
        }

        var named = images.FirstOrDefault(file =>
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            return stem.StartsWith("cover", StringComparison.OrdinalIgnoreCase)
                   || stem.StartsWith("preview", StringComparison.OrdinalIgnoreCase)
                   || stem.StartsWith("screenshot", StringComparison.OrdinalIgnoreCase);
        });
        return named ?? "";
    }

    internal static string InferCategory(GameProfile game, ParsedMod parsed)
    {
        if (parsed.Files.Any(f => IsPakFile(Path.GetFileName(f.RelativeDest))))
        {
            return "PAK";
        }

        if (parsed.Files.Any(f => f.RelativeDest.Replace('\\', '/').StartsWith("reframework/autorun", StringComparison.OrdinalIgnoreCase)))
        {
            return "脚本";
        }

        if (parsed.Files.Any(f => f.RelativeDest.Replace('\\', '/').StartsWith("reframework/plugins", StringComparison.OrdinalIgnoreCase)
                                  || Path.GetFileName(f.RelativeDest).Equals("dinput8.dll", StringComparison.OrdinalIgnoreCase)))
        {
            return "插件";
        }

        if (game.Id == GameId.World && parsed.Files.Any(f =>
                f.RelativeDest.Contains("/pl/", StringComparison.OrdinalIgnoreCase)
                || f.RelativeDest.Contains("/wp/", StringComparison.OrdinalIgnoreCase)
                || f.RelativeDest.Contains("\\pl\\", StringComparison.OrdinalIgnoreCase)
                || f.RelativeDest.Contains("\\wp\\", StringComparison.OrdinalIgnoreCase)))
        {
            return "模型替换";
        }

        if (game.UsesReFramework && parsed.Files.Any(f =>
                f.RelativeDest.Replace('\\', '/').StartsWith("natives/", StringComparison.OrdinalIgnoreCase)))
        {
            return "模型替换";
        }

        return "其他";
    }

    private static string CombineDest(string prefix, string relative)
    {
        var dest = string.IsNullOrEmpty(prefix) ? relative : $"{prefix}/{relative}";
        return dest.Replace('\\', '/').TrimStart('/');
    }

    private static bool DirExists(string root, string name) =>
        Directory.Exists(Path.Combine(root, name));

    private static bool HasAnyDir(string root, params string[] names) =>
        names.Any(n => Directory.GetDirectories(root, n, SearchOption.TopDirectoryOnly).Length > 0
                       || Directory.Exists(Path.Combine(root, n)));

    private static bool HasFile(string root, string name) =>
        File.Exists(Path.Combine(root, name));

    private static bool HasExtension(string root, string ext) =>
        Directory.GetFiles(root, "*" + ext, SearchOption.AllDirectories).Length > 0;

    private static bool HasGameDll(string root) =>
        Directory.GetFiles(root, "*.dll", SearchOption.TopDirectoryOnly)
            .Any(f => GameDlls.Contains(Path.GetFileName(f)));
}
