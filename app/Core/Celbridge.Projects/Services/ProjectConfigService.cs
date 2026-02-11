using System.Globalization;
using Celbridge.Python;
using Tomlyn;
using Tomlyn.Model;

namespace Celbridge.Projects.Services;

public partial class ProjectConfigService : IProjectConfigService
{
    private const char PathSeparator = '/';

    private readonly IPythonConfigService _pythonConfigService;

    private TomlTable _root = new();
    private ProjectConfig _config = new();

    public ProjectConfigService(IPythonConfigService pythonConfigService)
    {
        _pythonConfigService = pythonConfigService;
    }

    public Result InitializeFromFile(string configFilePath)
    {
        try
        {
            if (!File.Exists(configFilePath))
            {
                // Config file doesn't exist - create empty config and continue
                _root = new TomlTable();
                _config = new ProjectConfig();
                return Result.Ok();
            }

            var text = File.ReadAllText(configFilePath);
            var parse = Toml.Parse(text);
            if (parse.HasErrors)
            {
                var errors = string.Join("; ", parse.Diagnostics.Select(d => d.ToString()));
                // Log error but don't fail - create empty config and continue
                _root = new TomlTable();
                _config = new ProjectConfig();
                return Result.Fail($"TOML parse error(s): {errors}. Project loaded with empty configuration.");
            }

            _root = (TomlTable)parse.ToModel();
            _config = MapRootToModel(_root, _pythonConfigService.DefaultPythonVersion);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            // Log exception but don't fail - create empty config and continue
            _root = new TomlTable();
            _config = new ProjectConfig();
            return Result.Fail($"Failed to read TOML file: {configFilePath}. Project loaded with empty configuration.")
                         .WithException(ex);
        }
    }


    public ProjectConfig Config
    {
        get
        {
            _config = MapRootToModel(_root, _pythonConfigService.DefaultPythonVersion);
            return _config;
        }
    }

    public bool Contains(string pointer) =>
        JsonPointerToml.TryResolve(_root, pointer, out _, out _);

    public bool TryGet<T>(string pointer, out T? value)
    {
        value = default;
        if (!JsonPointerToml.TryResolve(_root, pointer, out var node, out _))
        {
            return false;
        }

        try
        {
            if (node is null)
            {
                return false;
            }

            if (node is T t)
            {
                value = t;
                return true;
            }

            object? coerced = node switch
            {
                string s when typeof(T) == typeof(string) => s,
                bool b when typeof(T) == typeof(bool) => b,
                long l when typeof(T) == typeof(long) => l,
                int i when typeof(T) == typeof(int) => i,
                double d when typeof(T) == typeof(double) => d,
                decimal m when typeof(T) == typeof(decimal) => m,
                DateTime dt when typeof(T) == typeof(DateTime) => dt,
                DateTimeOffset dto when typeof(T) == typeof(DateTimeOffset) => dto,
                TomlArray arr when typeof(T) == typeof(TomlArray) => arr,
                TomlTable tab when typeof(T) == typeof(TomlTable) => tab,
                _ => null
            };

            if (coerced is T ok)
            {
                value = ok;
                return true;
            }

            if (typeof(T) == typeof(string))
            {
                value = (T)(object)TomlValueToString(node);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static ProjectConfig MapRootToModel(TomlTable root, string defaultPythonVersion)
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
                    requiresPythonValue = defaultPythonVersion;
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

        return new ProjectConfig { Project = projectSection, Celbridge = celbridgeSection, Shortcuts = shortcutsSection };
    }

    /// <summary>
    /// Parse the [[shortcut]] array format from TOML.
    /// </summary>
    private static ShortcutsSection ParseShortcutsArray(TomlTableArray shortcutsArray)
    {
        var definitions = new List<ShortcutDefinition>();
        var validationErrors = new List<ShortcutValidationError>();

        for (int i = 0; i < shortcutsArray.Count; i++)
        {
            var shortcutTable = shortcutsArray[i];
            var shortcutIndex = i + 1; // 1-based index for user-friendly error messages

            // Parse required 'name' property
            string? name = null;
            if (shortcutTable.TryGetValue("name", out var nameObj) && nameObj is string nameStr)
            {
                name = nameStr;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                validationErrors.Add(new ShortcutValidationError(shortcutIndex, "name", "The 'name' property is required and cannot be empty."));
                continue; // Skip this shortcut entry
            }

            // Validate name doesn't start or end with separator
            if (name.StartsWith(PathSeparator) || name.EndsWith(PathSeparator))
            {
                validationErrors.Add(new ShortcutValidationError(shortcutIndex, "name", $"The 'name' property cannot start or end with '{PathSeparator}'."));
                continue;
            }

            // Validate no empty segments
            if (name.Contains($"{PathSeparator}{PathSeparator}"))
            {
                validationErrors.Add(new ShortcutValidationError(shortcutIndex, "name", $"The 'name' property cannot contain empty segments (consecutive '{PathSeparator}' characters)."));
                continue;
            }

            // Parse optional properties
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

            // Create the shortcut definition
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
        
        // Collect all group paths (items without scripts define groups)
        foreach (var def in definitions)
        {
            if (def.IsGroup)
            {
                groupPaths.Add(def.Name);
            }
        }

        // Validate that all non-top-level items have valid parent paths
        for (int i = 0; i < definitions.Count; i++)
        {
            var def = definitions[i];
            var parentPath = def.ParentPath;
            
            if (parentPath != null)
            {
                // Check if the immediate parent path exists as a group
                if (!groupPaths.Contains(parentPath))
                {
                    // Check if any ancestor exists as a group
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
                // Mark all ancestor paths as used
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
