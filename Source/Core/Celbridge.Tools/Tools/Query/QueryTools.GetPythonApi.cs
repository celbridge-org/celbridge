using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Celbridge.Python;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

public partial class QueryTools
{
    /// <summary>
    /// Returns the Python API reference for the cel proxy. Lists every available
    /// namespace, method, and its parameters with types and defaults. Use this when
    /// writing Python scripts that call Celbridge tools.
    /// </summary>
    /// <returns>A text document listing all cel proxy namespaces and method signatures with return types.</returns>
    [McpServerTool(Name = "query_get_python_api", ReadOnly = true, Idempotent = true)]
    [ToolAlias("query.get_python_api")]
    public partial CallToolResult GetPythonApi()
    {
        var reference = BuildPythonApiReference();
        return SuccessResult(reference);
    }

    private static string BuildPythonApiReference()
    {
        var toolAssembly = typeof(QueryTools).Assembly;
        var xmlReturns = LoadXmlReturnsDocumentation(toolAssembly);
        var toolMethods = FindToolMethods(toolAssembly, xmlReturns);

        var namespaces = new SortedDictionary<string, List<ToolMethodInfo>>();

        foreach (var toolMethod in toolMethods)
        {
            if (!namespaces.TryGetValue(toolMethod.Namespace, out var methods))
            {
                methods = new List<ToolMethodInfo>();
                namespaces[toolMethod.Namespace] = methods;
            }
            methods.Add(toolMethod);
        }

        var builder = new StringBuilder();
        builder.AppendLine("# Celbridge Python API Reference");
        builder.AppendLine();
        builder.AppendLine("Access tools via the `cel` proxy in the Python REPL.");
        builder.AppendLine("Import namespaces with: `from celbridge import app, document, file`");
        builder.AppendLine();
        builder.AppendLine("## Conventions");
        builder.AppendLine();
        builder.AppendLine("- Parameters use snake_case. JSON results are returned as dicts.");
        builder.AppendLine("- Errors raise `CelError` with a message string.");
        builder.AppendLine("- Methods marked `-> ok` return the string 'ok' on success or raise `CelError`.");
        builder.AppendLine("- Methods with no return annotation return `None`.");
        builder.AppendLine();
        builder.AppendLine("## Parameter Formats");
        builder.AppendLine();
        builder.AppendLine("Some parameters accept structured data as JSON-encoded strings. The proxy");
        builder.AppendLine("auto-serializes Python lists and dicts (including nested structures with");
        builder.AppendLine("str, int, float, bool, and None values) to JSON for these parameters,");
        builder.AppendLine("so you can pass native Python objects directly:");
        builder.AppendLine();
        builder.AppendLine("- `edits_json`: a list of edit dicts:");
        builder.AppendLine("  `[{\"line\": int, \"endLine\": int, \"newText\": str, \"column\"?: int (default 1), \"endColumn\"?: int (default -1 = end of line)}]`");
        builder.AppendLine("- `resources` (in file.read_many): a list of resource key strings: `[\"a.txt\", \"scripts/b.py\"]`");
        builder.AppendLine("- `files` (in file.grep): a list of resource key strings: `[\"a.txt\", \"scripts/b.py\"]`");
        builder.AppendLine("- `file_resource` (in document.close): a single resource key or a list: `\"a.txt\"` or `[\"a.txt\", \"b.txt\"]`");
        builder.AppendLine();

        foreach (var (namespaceName, methods) in namespaces)
        {
            builder.AppendLine($"## {namespaceName}");
            builder.AppendLine();

            methods.Sort((a, b) => string.Compare(a.MethodName, b.MethodName, StringComparison.Ordinal));

            foreach (var method in methods)
            {
                var returnAnnotation = FormatReturnAnnotation(method.Returns, method.IsVoid);
                builder.AppendLine($"  {namespaceName}.{method.MethodName}({method.ParameterSignature}){returnAnnotation}");
            }

            builder.AppendLine();
        }

        var installedPackages = PythonEnvironmentInfo.InstalledPackages;
        if (installedPackages.Count > 0)
        {
            builder.AppendLine("## Installed Packages");
            builder.AppendLine();
            foreach (var packageEntry in installedPackages)
            {
                builder.AppendLine($"  {packageEntry}");
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static Dictionary<string, string> LoadXmlReturnsDocumentation(Assembly assembly)
    {
        var result = new Dictionary<string, string>();

        var assemblyLocation = assembly.Location;
        if (string.IsNullOrEmpty(assemblyLocation))
        {
            return result;
        }

        var xmlPath = Path.ChangeExtension(assemblyLocation, ".xml");
        if (!File.Exists(xmlPath))
        {
            return result;
        }

        try
        {
            var document = XDocument.Load(xmlPath);
            var members = document.Descendants("member");

            foreach (var member in members)
            {
                var nameAttribute = member.Attribute("name")?.Value;
                if (nameAttribute is null || !nameAttribute.StartsWith("M:"))
                {
                    continue;
                }

                var returnsElement = member.Element("returns");
                if (returnsElement is null)
                {
                    continue;
                }

                var returnsText = returnsElement.Value.Trim();
                if (!string.IsNullOrEmpty(returnsText))
                {
                    result[nameAttribute] = returnsText;
                }
            }
        }
        catch
        {
            // If XML parsing fails, continue without returns documentation
        }

        return result;
    }

    private static List<ToolMethodInfo> FindToolMethods(Assembly assembly, Dictionary<string, string> xmlReturns)
    {
        var results = new List<ToolMethodInfo>();

        foreach (var type in assembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
                var aliasAttribute = method.GetCustomAttribute<ToolAliasAttribute>();

                if (toolAttribute is null || aliasAttribute is null)
                {
                    continue;
                }

                var alias = aliasAttribute.Alias;
                var dotIndex = alias.IndexOf('.');
                var namespaceName = dotIndex >= 0 ? alias[..dotIndex] : alias;
                var methodName = dotIndex >= 0 ? alias[(dotIndex + 1)..] : alias;

                var parameterSignature = BuildParameterSignature(method);
                var returnsText = FindReturnsDocumentation(method, xmlReturns);
                var isVoid = method.ReturnType == typeof(void);

                results.Add(new ToolMethodInfo(namespaceName, methodName, parameterSignature, returnsText, isVoid));
            }
        }

        return results;
    }

    private static string FindReturnsDocumentation(MethodInfo method, Dictionary<string, string> xmlReturns)
    {
        // Build the XML doc member name for this method
        var declaringType = method.DeclaringType;
        if (declaringType is null)
        {
            return "";
        }

        var typeName = declaringType.FullName?.Replace('+', '.');
        var memberName = $"M:{typeName}.{method.Name}";

        // Try exact match first (no parameters)
        if (xmlReturns.TryGetValue(memberName, out var returnsText))
        {
            return returnsText;
        }

        // Try with parameter types for overloaded methods
        var parameters = method.GetParameters();
        if (parameters.Length > 0)
        {
            var parameterTypes = string.Join(",", parameters.Select(p => p.ParameterType.FullName));
            var memberNameWithParams = $"{memberName}({parameterTypes})";
            if (xmlReturns.TryGetValue(memberNameWithParams, out returnsText))
            {
                return returnsText;
            }
        }

        return "";
    }

    private static string FormatReturnAnnotation(string returns, bool isVoid)
    {
        if (isVoid)
        {
            return " -> None";
        }

        if (string.IsNullOrEmpty(returns))
        {
            return "";
        }

        // Simplify the common action tool pattern to just "ok"
        if (returns.Contains("\"ok\" on success"))
        {
            return " -> ok";
        }

        return $" -> {returns}";
    }

    private static string BuildParameterSignature(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var parts = new List<string>();

        foreach (var parameter in parameters)
        {
            var parameterName = ToSnakeCase(parameter.Name ?? "");
            var typeName = MapTypeName(parameter.ParameterType);

            if (parameter.HasDefaultValue)
            {
                var defaultValue = FormatDefaultValue(parameter.DefaultValue, parameter.ParameterType);
                parts.Add($"{parameterName}: {typeName} = {defaultValue}");
            }
            else
            {
                parts.Add($"{parameterName}: {typeName}");
            }
        }

        return string.Join(", ", parts);
    }

    private static string MapTypeName(Type type)
    {
        if (type == typeof(string)) return "str";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "int";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(double)) return "float";
        if (type == typeof(float)) return "float";
        return "str";
    }

    private static string FormatDefaultValue(object? value, Type parameterType)
    {
        if (value is null) return "None";
        if (value is bool boolValue) return boolValue ? "True" : "False";
        if (value is string stringValue) return stringValue == "" ? "''" : $"'{stringValue}'";
        return value.ToString() ?? "None";
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var builder = new StringBuilder();
        builder.Append(char.ToLowerInvariant(name[0]));

        for (int i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]))
            {
                builder.Append('_');
                builder.Append(char.ToLowerInvariant(name[i]));
            }
            else
            {
                builder.Append(name[i]);
            }
        }

        return builder.ToString();
    }
}

internal record class ToolMethodInfo(string Namespace, string MethodName, string ParameterSignature, string Returns, bool IsVoid);
