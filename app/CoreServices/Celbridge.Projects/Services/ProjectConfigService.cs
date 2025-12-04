using System.Globalization;
using Tomlyn;
using Tomlyn.Model;
using Humanizer;

namespace Celbridge.Projects.Services;

public partial class ProjectConfigService : IProjectConfigService
{
    private TomlTable _root = new();
    private ProjectConfig _config = new();

    public Result InitializeFromFile(string configFilePath)
    {
        try
        {
            var text = File.ReadAllText(configFilePath);
            var parse = Toml.Parse(text);
            if (parse.HasErrors)
            {
                var errors = string.Join("; ", parse.Diagnostics.Select(d => d.ToString()));
                return Result.Fail($"TOML parse error(s): {errors}");
            }

            _root = (TomlTable)parse.ToModel();
            _config = MapRootToModel(_root);

            // Notify Main Page to allow UI updates for user functions.
            IProjectService projectService = ServiceLocator.AcquireService<IProjectService>();
            projectService.InvokeRebuildUserFunctionsUI(Config.NavigationBar);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to read TOML file: {configFilePath}")
                         .WithException(ex);
        }
    }


    public ProjectConfig Config
    {
        get
        {
            _config = MapRootToModel(_root);
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

    private static ProjectConfig MapRootToModel(TomlTable root)
    {
        var projectSection = new ProjectSection();
        var celbridgeSection = new CelbridgeSection();
        var navigationBarSection = new NavigationBarSection();

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

            projectSection = projectSection with
            {
                Name = projectTable.TryGetValue("name", out var name) ? name?.ToString() : null,
                Version = projectTable.TryGetValue("version", out var version) ? version?.ToString() : null,
                RequiresPython = projectTable.TryGetValue("requires-python", out var requiresPython) ? requiresPython?.ToString() : null,
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
                Version = celbridgeTable.TryGetValue("version", out var ver) ? ver?.ToString() : null,
                Scripts = scriptsDict
            };
        }

        // [navigation_bar]
        if (root.TryGetValue("navigation_bar", out var navigationBarObject) && 
            navigationBarObject is TomlTable navigationBarTable)
        {
            ExtractNavigationBarEntry(navigationBarTable, navigationBarSection.RootCustomCommandNode, "Root", null);
        }

        return new ProjectConfig { Project = projectSection, Celbridge = celbridgeSection, NavigationBar = navigationBarSection };
    }

    private static bool CheckTableHasSubTable(TomlTable table)
    {
        foreach (var (k, v) in table)
        {
            if (v is TomlTable)
            {
                return true;
            }
        }

        return false;
    }

    private static void ExtractNavigationBarEntry(TomlTable barEntry, NavigationBarSection.CustomCommandNode node, string name, NavigationBarSection.CustomCommandNode? previousNode, string path = "")
    {
        foreach (var (k, v) in barEntry)
        {
            var key = k.Humanize(LetterCasing.Title);

            if (v is TomlTable table)
            {
                string newPath = path + (path.Length > 0 ? "." : "") + key;

                if (node.Nodes.ContainsKey(key))
                {
                    ExtractNavigationBarEntry(table, node.Nodes[key], key, node, newPath);
                }
                else
                {
                    if (CheckTableHasSubTable(table))
                    {
                        var newNode = new NavigationBarSection.CustomCommandNode();
                        newNode.Path = path;
                        node.Nodes.Add(key, newNode);
                        ExtractNavigationBarEntry(table, newNode, key, node, newPath);
                    }
                    else
                    {
                        ExtractNavigationBarEntry(table, node, key, node, newPath);
                    }
                }
            }
        }

        var customCommandDefinition = new NavigationBarSection.CustomCommandDefinition()
        {
            Icon = barEntry.TryGetValue("icon", out var icon) ? icon?.ToString() : null,
            ToolTip = barEntry.TryGetValue("tooltip", out var tooltip) ? tooltip?.ToString() : null,
            Script = barEntry.TryGetValue("script", out var script) ? script?.ToString() : null,
            Name = barEntry.TryGetValue("name", out var givenName) ? givenName?.ToString() : name.Humanize(LetterCasing.Title),
            Path = path
        };

        if (customCommandDefinition.Icon != null || customCommandDefinition.ToolTip != null || customCommandDefinition.Script != null)
        {
            if (previousNode == null)
            {
                throw new Exception("Commands must not be unnamed on root");
            }
            else
            {
                previousNode!.CustomCommands.Add(customCommandDefinition);
            }
        }
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
