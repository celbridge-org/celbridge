using System.Globalization;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Projects.Services;

/// <summary>
/// Static utility class for parsing Celbridge project configuration files.
/// </summary>
public static class ProjectConfigParser
{
    private const char PathSeparator = '/';
    private const string DefaultPythonVersion = "3.12";

    /// <summary>
    /// Parses a project config from a .celbridge file.
    /// Returns an empty config if the file doesn't exist.
    /// </summary>
    public static Result<ProjectConfig> ParseFromFile(string configFilePath)
    {
        try
        {
            if (!File.Exists(configFilePath))
            {
                return Result<ProjectConfig>.Ok(new ProjectConfig());
            }

            var text = File.ReadAllText(configFilePath);
            var parse = Toml.Parse(text);
            if (parse.HasErrors)
            {
                var errors = string.Join("; ", parse.Diagnostics.Select(d => d.ToString()));
                return Result<ProjectConfig>.Fail($"TOML parse error(s): {errors}");
            }

            var root = (TomlTable)parse.ToModel();
            var config = MapRootToModel(root);

            return Result<ProjectConfig>.Ok(config);
        }
        catch (Exception ex)
        {
            return Result<ProjectConfig>.Fail($"Failed to read TOML file: {configFilePath}")
                .WithException(ex);
        }
    }

    private static ProjectConfig MapRootToModel(TomlTable root)
    {
        var projectSection = new ProjectSection();
        var celbridgeSection = new CelbridgeSection();
        var shortcutsSection = new ShortcutsSection();

        // [project]
        if (root.TryGetValue("project", out var projectObject) &&
            projectObject is TomlTable projectTable)
        {
            var propertiesDict = new Dictionary<string, string>();
            if (projectTable.TryGetValue("properties", out var propertiesObject) &&
                propertiesObject is TomlTable propertiesTable)
            {
                foreach (var (k, v) in propertiesTable)
                {
                    propertiesDict[k] = TomlValueToString(v);
                }
            }

            List<string>? dependencies = null;
            if (projectTable.TryGetValue("dependencies", out var dependenciesObject) &&
                dependenciesObject is TomlArray dependenciesArray)
            {
                dependencies = dependenciesArray.Select(x => x?.ToString() ?? string.Empty).ToList();
            }

            string? requiresPythonValue = null;
            if (projectTable.TryGetValue("requires-python", out var requiresPython))
            {
                requiresPythonValue = requiresPython?.ToString();
                if (requiresPythonValue == "<python-version>")
                {
                    requiresPythonValue = DefaultPythonVersion;
                }
            }

            projectSection = projectSection with
            {
                Name = projectTable.TryGetValue("name", out var name) ? name?.ToString() : null,
                Version = projectTable.TryGetValue("version", out var pythonVersion) ? pythonVersion?.ToString() : null,
                RequiresPython = requiresPythonValue,
                Dependencies = dependencies,
                Properties = propertiesDict
            };
        }

        // [celbridge]
        if (root.TryGetValue("celbridge", out var celbridgeObject) &&
            celbridgeObject is TomlTable celbridgeTable)
        {
            var scriptsDict = new Dictionary<string, string>();
            if (celbridgeTable.TryGetValue("scripts", out var scriptsObject) &&
                scriptsObject is TomlTable scriptsTable)
            {
                foreach (var (k, v) in scriptsTable)
                {
                    scriptsDict[k] = v?.ToString() ?? string.Empty;
                }
            }

            celbridgeSection = celbridgeSection with
            {
                Version = celbridgeTable.TryGetValue("celbridge-version", out var celbridgeVersion) ? celbridgeVersion?.ToString() : null,
                Scripts = scriptsDict
            };
        }

        // [[shortcut]]
        if (root.TryGetValue("shortcut", out var shortcutsObject) &&
            shortcutsObject is TomlTableArray shortcutsArray)
        {
            shortcutsSection = ParseShortcutsArray(shortcutsArray);
        }

        // [features]
        var featuresDict = new Dictionary<string, bool>();
        if (root.TryGetValue("features", out var featuresObject) &&
            featuresObject is TomlTable featuresTable)
        {
            foreach (var (k, v) in featuresTable)
            {
                if (v is bool boolValue)
                {
                    featuresDict[k] = boolValue;
                }
            }
        }

        return new ProjectConfig
        {
            Project = projectSection,
            Celbridge = celbridgeSection,
            Shortcuts = shortcutsSection,
            Features = featuresDict
        };
    }

    private static ShortcutsSection ParseShortcutsArray(TomlTableArray shortcutsArray)
    {
        var definitions = new List<ShortcutDefinition>();
        var validationErrors = new List<ShortcutValidationError>();

        for (int i = 0; i < shortcutsArray.Count; i++)
        {
            var shortcutTable = shortcutsArray[i];
            var shortcutIndex = i + 1;

            string? name = null;
            if (shortcutTable.TryGetValue("name", out var nameObj) && nameObj is string nameStr)
            {
                name = nameStr;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                validationErrors.Add(new ShortcutValidationError(shortcutIndex, "name", "The 'name' property is required and cannot be empty."));
                continue;
            }

            if (name.StartsWith(PathSeparator) || name.EndsWith(PathSeparator))
            {
                validationErrors.Add(new ShortcutValidationError(shortcutIndex, "name", $"The 'name' property cannot start or end with '{PathSeparator}'."));
                continue;
            }

            if (name.Contains($"{PathSeparator}{PathSeparator}"))
            {
                validationErrors.Add(new ShortcutValidationError(shortcutIndex, "name", $"The 'name' property cannot contain empty segments (consecutive '{PathSeparator}' characters)."));
                continue;
            }

            string? icon = null;
            if (shortcutTable.TryGetValue("icon", out var iconObj) && iconObj is string iconStr)
            {
                icon = iconStr;
            }

            string? tooltip = null;
            if (shortcutTable.TryGetValue("tooltip", out var tooltipObj) && tooltipObj is string tooltipStr)
            {
                tooltip = tooltipStr;
            }

            string? script = null;
            if (shortcutTable.TryGetValue("script", out var scriptObj) && scriptObj is string scriptStr)
            {
                script = scriptStr;
            }

            var definition = new ShortcutDefinition
            {
                Name = name,
                Icon = icon,
                Tooltip = tooltip,
                Script = script
            };

            definitions.Add(definition);
        }

        // Second pass validation: check that all parent paths exist as groups
        var groupPaths = new HashSet<string>();
        foreach (var def in definitions)
        {
            if (def.IsGroup)
            {
                groupPaths.Add(def.Name);
            }
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            var def = definitions[i];
            var parentPath = def.ParentPath;

            if (parentPath != null)
            {
                if (!groupPaths.Contains(parentPath))
                {
                    var pathSegments = parentPath.Split(PathSeparator);
                    var currentPath = "";
                    bool foundValidParent = false;

                    foreach (var segment in pathSegments)
                    {
                        currentPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}{PathSeparator}{segment}";
                        if (groupPaths.Contains(currentPath))
                        {
                            foundValidParent = true;
                        }
                    }

                    if (!foundValidParent)
                    {
                        validationErrors.Add(new ShortcutValidationError(
                            i + 1,
                            "name",
                            $"The parent path '{parentPath}' does not exist. Define a group with name='{parentPath}' first."));
                    }
                }
            }
        }

        // Validate that groups have at least one child
        var usedParentPaths = new HashSet<string>();
        foreach (var def in definitions)
        {
            var parentPath = def.ParentPath;
            if (parentPath != null)
            {
                var pathSegments = parentPath.Split(PathSeparator);
                var currentPath = "";
                foreach (var segment in pathSegments)
                {
                    currentPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}{PathSeparator}{segment}";
                    usedParentPaths.Add(currentPath);
                }
            }
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            var def = definitions[i];
            if (def.IsGroup && !usedParentPaths.Contains(def.Name))
            {
                validationErrors.Add(new ShortcutValidationError(
                    i + 1,
                    "script",
                    $"Group '{def.DisplayName}' has no children. Either add child items with names starting with '{def.Name}/' or add a script to make it a command."));
            }
        }

        return new ShortcutsSection
        {
            Definitions = definitions,
            ValidationErrors = validationErrors
        };
    }

    private static string TomlValueToString(object? value) =>
        value switch
        {
            null => string.Empty,
            string s => s,
            bool b => b ? "true" : "false",
            sbyte or byte or short or ushort or int or uint or long or ulong
                => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            float or double or decimal
                => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
            TomlArray arr => "[" + string.Join(",", arr.Select(FormatArrayItem)) + "]",
            TomlTable => "<table>",
            _ => value.ToString() ?? string.Empty
        };

    private static string FormatArrayItem(object? item) =>
        item switch
        {
            null => "null",
            string s => $"\"{s.Replace("\"", "\\\"")}\"",
            bool b => b ? "true" : "false",
            sbyte or byte or short or ushort or int or uint or long or ulong
                => Convert.ToString(item, CultureInfo.InvariantCulture) ?? "0",
            float or double or decimal
                => Convert.ToString(item, CultureInfo.InvariantCulture) ?? "0",
            DateTime dt => $"\"{dt:O}\"",
            DateTimeOffset dto => $"\"{dto:O}\"",
            TomlTable or TomlArray => "\"<complex>\"",
            _ => $"\"{item.ToString()?.Replace("\"", "\\\"")}\""
        };
}
