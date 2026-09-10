using System.Xml.Linq;
using MhModManager.Models;

namespace MhModManager.Core;

internal static class ModuleConfigParser
{
    public static ParsedMod Parse(GameProfile game, string sourceFile, string stagingDir, string configPath)
    {
        var document = XDocument.Load(configPath, LoadOptions.PreserveWhitespace);
        var config = document.Root ?? throw new InvalidDataException("ModuleConfig.xml 缺少 config 根节点");
        var configRoot = Path.GetDirectoryName(configPath)!;
        var parsed = new ParsedMod
        {
            Name = Value(config, "moduleName") ?? Path.GetFileNameWithoutExtension(sourceFile),
            Version = Value(config, "version") ?? File.GetLastWriteTime(sourceFile).ToString("yyyy-MM-dd"),
            Author = Value(config, "author") ?? "",
            Description = Value(config, "description") ?? "",
            SourceFile = sourceFile,
            StagingDir = stagingDir
        };

        NexusNames.Apply(parsed, sourceFile);
        var image = Child(config, "moduleImage")?.Attribute("path")?.Value;
        if (!string.IsNullOrWhiteSpace(image))
        {
            var imagePath = ResolveSource(configRoot, image);
            if (File.Exists(imagePath))
            {
                parsed.PreviewSource = imagePath;
            }
        }

        var selectedFiles = new Dictionary<string, (ParsedFile File, int Priority)>(StringComparer.OrdinalIgnoreCase);
        AddFileList(Child(config, "requiredInstallFiles"), configRoot, selectedFiles);

        var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var installSteps = Child(config, "installSteps");
        if (installSteps is not null)
        {
            foreach (var step in Children(installSteps, "installStep"))
            {
                var visible = Child(step, "visible");
                if (visible is not null && !EvaluateDependencies(visible, flags))
                {
                    continue;
                }

                var groups = Child(step, "optionalFileGroups");
                if (groups is null)
                {
                    continue;
                }

                foreach (var group in Children(groups, "group"))
                {
                    var plugins = Child(group, "plugins");
                    if (plugins is null)
                    {
                        continue;
                    }

                    foreach (var plugin in SelectDefaultPlugins(group, Children(plugins, "plugin").ToList()))
                    {
                        AddFileList(Child(plugin, "files"), configRoot, selectedFiles);
                        var conditionFlags = Child(plugin, "conditionFlags");
                        if (conditionFlags is null)
                        {
                            continue;
                        }

                        foreach (var flag in Children(conditionFlags, "flag"))
                        {
                            var name = flag.Attribute("name")?.Value;
                            var value = flag.Value?.Trim();
                            if (!string.IsNullOrWhiteSpace(name))
                            {
                                flags[name] = value ?? "";
                            }
                        }
                    }
                }
            }
        }

        var conditionalInstalls = Child(config, "conditionalFileInstalls");
        var patterns = conditionalInstalls is null ? null : Child(conditionalInstalls, "patterns");
        if (patterns is not null)
        {
            foreach (var pattern in Children(patterns, "pattern"))
            {
                var dependencies = Child(pattern, "dependencies");
                if (dependencies is null || EvaluateDependencies(dependencies, flags))
                {
                    AddFileList(Child(pattern, "files"), configRoot, selectedFiles);
                }
            }
        }

        parsed.Files.AddRange(selectedFiles.Values
            .OrderBy(item => item.Priority)
            .Select(item =>
            {
                item.File.RelativeDest = ModLayoutParser.RemapDestination(game, item.File.RelativeDest);
                return item.File;
            })
            .Where(file => !Path.GetFileName(file.RelativeDest).Equals("ModuleConfig.xml", StringComparison.OrdinalIgnoreCase)));
        parsed.Category = ModLayoutParser.InferCategory(game, parsed);
        return parsed;
    }

    private static IEnumerable<XElement> SelectDefaultPlugins(XElement group, List<XElement> plugins)
    {
        var usable = plugins.Where(plugin => PluginType(plugin) != "NotUsable").ToList();
        var required = usable.Where(plugin => PluginType(plugin) == "Required").ToList();
        var recommended = usable.Where(plugin => PluginType(plugin) == "Recommended").ToList();
        var groupType = group.Attribute("type")?.Value ?? "SelectAny";

        if (groupType == "SelectAll")
        {
            return usable;
        }

        if (groupType is "SelectExactlyOne" or "SelectAtMostOne")
        {
            var selected = required.FirstOrDefault() ?? recommended.FirstOrDefault();
            if (selected is null && groupType == "SelectExactlyOne")
            {
                selected = usable.FirstOrDefault();
            }

            return selected is null ? [] : [selected];
        }

        var defaults = required.Concat(recommended).Distinct().ToList();
        if (groupType == "SelectAtLeastOne" && defaults.Count == 0 && usable.Count > 0)
        {
            defaults.Add(usable[0]);
        }

        return defaults;
    }

    private static string PluginType(XElement plugin)
    {
        var descriptor = Child(plugin, "typeDescriptor");
        var type = descriptor is null ? null : Child(descriptor, "type");
        return type?.Attribute("name")?.Value ?? "Optional";
    }

    private static void AddFileList(
        XElement? list,
        string configRoot,
        Dictionary<string, (ParsedFile File, int Priority)> files)
    {
        if (list is null)
        {
            return;
        }

        foreach (var item in list.Elements())
        {
            var kind = item.Name.LocalName;
            if (kind is not ("file" or "folder"))
            {
                continue;
            }

            var sourceValue = item.Attribute("source")?.Value;
            if (string.IsNullOrWhiteSpace(sourceValue))
            {
                continue;
            }

            var destination = NormalizeDestination(item.Attribute("destination")?.Value);
            var priority = int.TryParse(item.Attribute("priority")?.Value, out var parsedPriority) ? parsedPriority : -1;
            var source = ResolveSource(configRoot, sourceValue);
            if (kind == "file")
            {
                var target = string.IsNullOrWhiteSpace(destination) ? Path.GetFileName(source) : destination;
                Add(source, target, priority, files);
                continue;
            }

            if (!Directory.Exists(source))
            {
                throw new InvalidDataException($"ModuleConfig.xml 引用的目录不存在: {sourceValue}");
            }

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                var target = string.IsNullOrWhiteSpace(destination) ? relative : Combine(destination, relative);
                Add(file, target, priority, files);
            }
        }
    }

    private static void Add(
        string source,
        string destination,
        int priority,
        Dictionary<string, (ParsedFile File, int Priority)> files)
    {
        if (!File.Exists(source))
        {
            throw new InvalidDataException($"ModuleConfig.xml 引用的文件不存在: {source}");
        }

        var normalized = NormalizeDestination(destination);
        if (!files.TryGetValue(normalized, out var existing) || priority >= existing.Priority)
        {
            files[normalized] = (new ParsedFile { SourcePath = source, RelativeDest = normalized }, priority);
        }
    }

    private static bool EvaluateDependencies(XElement dependencies, IReadOnlyDictionary<string, string> flags)
    {
        var results = new List<bool>();
        foreach (var dependency in dependencies.Elements())
        {
            switch (dependency.Name.LocalName)
            {
                case "flagDependency":
                    var flag = dependency.Attribute("flag")?.Value;
                    var expected = dependency.Attribute("value")?.Value ?? "";
                    results.Add(flag is not null && flags.TryGetValue(flag, out var actual) && actual == expected);
                    break;
                case "dependencies":
                    results.Add(EvaluateDependencies(dependency, flags));
                    break;
                default:
                    results.Add(true);
                    break;
            }
        }

        if (results.Count == 0)
        {
            return true;
        }

        return dependencies.Attribute("operator")?.Value == "Or" ? results.Any(value => value) : results.All(value => value);
    }

    private static string ResolveSource(string root, string relative)
    {
        var path = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(root, path));
    }

    private static string NormalizeDestination(string? path) =>
        (path ?? "").Replace('\\', '/').Trim('/');

    private static string Combine(string left, string right) =>
        $"{left.TrimEnd('/')}/{right.TrimStart('/')}";

    private static XElement? Child(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == name);

    private static IEnumerable<XElement> Children(XElement parent, string name) =>
        parent.Elements().Where(element => element.Name.LocalName == name);

    private static string? Value(XElement parent, string name) => Child(parent, name)?.Value.Trim();
}
