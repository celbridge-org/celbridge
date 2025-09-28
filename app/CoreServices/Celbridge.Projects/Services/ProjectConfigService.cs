using System.Globalization;
using Tomlyn;
using Tomlyn.Model;

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
        var pythonSection = new PythonSection();

        // [project]
        if (root.TryGetValue("project", out var projObj) && projObj is TomlTable proj)
        {
            var propsDict = new Dictionary<string, string>();
            if (proj.TryGetValue("properties", out var propsObj) && propsObj is TomlTable props)
            {
                foreach (var (k, v) in props)
                {
                    propsDict[k] = TomlValueToString(v);
                }
            }

            projectSection = projectSection with
            {
                ProjectVersion = proj.TryGetValue("project_version", out var pv) ? pv?.ToString() : null,
                CelbridgeVersion = proj.TryGetValue("celbridge_version", out var cv) ? cv?.ToString() : null,
                Properties = propsDict
            };
        }

        // [python]
        if (root.TryGetValue("python", out var pyObj) && pyObj is TomlTable py)
        {
            List<string>? packages = null;
            if (py.TryGetValue("packages", out var packagesObj) && packagesObj is TomlArray packagesArray)
            {
                packages = packagesArray.Select(x => x?.ToString() ?? string.Empty).ToList();
            }

            var scriptsDict = new Dictionary<string, string>();
            if (py.TryGetValue("scripts", out var scriptsObj) && scriptsObj is TomlTable scripts)
            {
                foreach (var (k, v) in scripts)
                {
                    scriptsDict[k] = v?.ToString() ?? string.Empty;
                }
            }

            pythonSection = pythonSection with
            {
                Version = py.TryGetValue("version", out var ver) ? ver?.ToString() : null,
                Packages = packages,
                Scripts = scriptsDict
            };
        }

        return new ProjectConfig { Project = projectSection, Python = pythonSection };
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
