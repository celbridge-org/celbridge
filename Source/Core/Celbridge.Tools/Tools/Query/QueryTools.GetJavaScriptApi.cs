using System.Reflection;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class QueryTools
{
    /// <summary>
    /// Returns the JavaScript API reference for the cel proxy exposed to package
    /// extensions. Lists every available namespace, method, and its parameters
    /// with TypeScript-style types and defaults. Use this when writing JavaScript
    /// package extensions that call Celbridge tools.
    /// </summary>
    /// <returns>A text document listing all cel proxy namespaces and method signatures with Promise return types.</returns>
    [McpServerTool(Name = "query_get_javascript_api", ReadOnly = true, Idempotent = true)]
    [ToolAlias("query.get_javascript_api")]
    public partial CallToolResult GetJavaScriptApi()
    {
        var reference = BuildJavaScriptApiReference();
        return SuccessResult(reference);
    }

    private static string BuildJavaScriptApiReference()
    {
        var toolAssembly = typeof(QueryTools).Assembly;
        var xmlReturns = ToolApiReflection.LoadXmlReturnsDocumentation(toolAssembly);
        var toolMethods = ToolApiReflection.FindToolMethods(toolAssembly, xmlReturns, BuildJavaScriptParameterSignature);

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
        var header = LoadEmbeddedResource("Celbridge.Tools.Assets.JavaScriptApiHeader.md");
        builder.AppendLine(header.TrimEnd());
        builder.AppendLine();

        foreach (var (namespaceName, methods) in namespaces)
        {
            builder.AppendLine($"## {namespaceName}");
            builder.AppendLine();

            methods.Sort((a, b) => string.Compare(a.MethodName, b.MethodName, StringComparison.Ordinal));

            foreach (var method in methods)
            {
                var camelMethodName = ToCamelCase(method.MethodName);
                var returnAnnotation = FormatJavaScriptReturnAnnotation(method.Returns, method.IsVoid);
                builder.AppendLine($"  cel.{namespaceName}.{camelMethodName}({method.ParameterSignature}){returnAnnotation}");
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatJavaScriptReturnAnnotation(string returns, bool isVoid)
    {
        if (isVoid)
        {
            return ": Promise<void>";
        }

        if (string.IsNullOrEmpty(returns))
        {
            return ": Promise<any>";
        }

        // Simplify the common action tool pattern to Promise<"ok">.
        if (returns.Contains("\"ok\" on success"))
        {
            return ": Promise<\"ok\">";
        }

        return $": Promise<{returns}>";
    }

    private static string BuildJavaScriptParameterSignature(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var parts = new List<string>();

        foreach (var parameter in parameters)
        {
            var parameterName = ToCamelCase(parameter.Name ?? "");
            var typeName = MapJavaScriptTypeName(parameter.ParameterType);

            if (parameter.HasDefaultValue)
            {
                var defaultValue = FormatJavaScriptDefaultValue(parameter.DefaultValue, parameter.ParameterType);
                parts.Add($"{parameterName}: {typeName} = {defaultValue}");
            }
            else
            {
                parts.Add($"{parameterName}: {typeName}");
            }
        }

        return string.Join(", ", parts);
    }

    private static string MapJavaScriptTypeName(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(int)) return "number";
        if (type == typeof(long)) return "number";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(double)) return "number";
        if (type == typeof(float)) return "number";
        return "string";
    }

    private static string FormatJavaScriptDefaultValue(object? value, Type parameterType)
    {
        if (value is null) return "null";
        if (value is bool boolValue) return boolValue ? "true" : "false";
        if (value is string stringValue) return stringValue == "" ? "\"\"" : $"\"{stringValue}\"";
        return value.ToString() ?? "null";
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        // The input is either a camelCase C# parameter name (already correct) or a
        // snake_case tool alias segment (e.g. "get_status"). Handle both by
        // collapsing underscores and uppercasing the following character.
        var builder = new StringBuilder(name.Length);
        var upperNext = false;

        for (int i = 0; i < name.Length; i++)
        {
            var character = name[i];
            if (character == '_')
            {
                upperNext = true;
                continue;
            }

            if (i == 0)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (upperNext)
            {
                builder.Append(char.ToUpperInvariant(character));
                upperNext = false;
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
