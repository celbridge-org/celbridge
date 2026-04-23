using System.Reflection;
using System.Xml.Linq;
using ModelContextProtocol.Server;
using Path = System.IO.Path;

namespace Celbridge.Tools;

/// <summary>
/// Shared reflection helpers for discovering MCP tool methods and their XML
/// returns documentation. Used by the language-specific API reference tools
/// (e.g. query_get_python_api, query_get_javascript_api) to build their
/// output. Language-specific formatting (type names, parameter signatures,
/// return annotations) lives in the tool classes themselves.
/// </summary>
internal static class ToolApiReflection
{
    /// <summary>
    /// Loads the /// returns XML documentation for every method in the assembly,
    /// keyed by the XML doc member name (e.g. "M:Celbridge.Tools.AppTools.ShowAlert").
    /// Returns an empty dictionary if the XML file is missing or cannot be parsed.
    /// </summary>
    public static Dictionary<string, string> LoadXmlReturnsDocumentation(Assembly assembly)
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
            // If XML parsing fails, continue without returns documentation.
        }

        return result;
    }

    /// <summary>
    /// Discovers every MCP tool method in the assembly. A method is considered a
    /// tool when it carries both an McpServerTool and a ToolAlias attribute.
    /// The parameter signature is built by the caller-supplied formatter so that
    /// each language can apply its own type mapping and naming conventions.
    /// </summary>
    public static List<ToolMethodInfo> FindToolMethods(
        Assembly assembly,
        Dictionary<string, string> xmlReturns,
        Func<MethodInfo, string> buildParameterSignature)
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

                var parameterSignature = buildParameterSignature(method);
                var returnsText = FindReturnsDocumentation(method, xmlReturns);
                var isVoid = method.ReturnType == typeof(void);

                results.Add(new ToolMethodInfo(namespaceName, methodName, parameterSignature, returnsText, isVoid));
            }
        }

        return results;
    }

    /// <summary>
    /// Looks up the /// returns text for a method by building its XML doc member
    /// name. Tries the unparameterised form first, then falls back to the
    /// overload-disambiguated form ("M:Type.Method(ParamTypes)").
    /// </summary>
    public static string FindReturnsDocumentation(MethodInfo method, Dictionary<string, string> xmlReturns)
    {
        var declaringType = method.DeclaringType;
        if (declaringType is null)
        {
            return "";
        }

        var typeName = declaringType.FullName?.Replace('+', '.');
        var memberName = $"M:{typeName}.{method.Name}";

        if (xmlReturns.TryGetValue(memberName, out var returnsText))
        {
            return returnsText;
        }

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
}

/// <summary>
/// Reflected metadata for a single MCP tool method, consumed by the
/// language-specific API reference renderers.
/// </summary>
internal record class ToolMethodInfo(
    string Namespace,
    string MethodName,
    string ParameterSignature,
    string Returns,
    bool IsVoid);
