using System.Reflection;
using System.Text;
using Celbridge.Python;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

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
        var xmlReturns = ToolApiReflection.LoadXmlReturnsDocumentation(toolAssembly);
        var toolMethods = ToolApiReflection.FindToolMethods(toolAssembly, xmlReturns, BuildParameterSignature);

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
        var header = LoadEmbeddedResource("Celbridge.Tools.Assets.PythonApiHeader.md");
        builder.AppendLine(header.TrimEnd());
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

        // Simplify the common action tool pattern to just "ok".
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
