using System.Reflection;
using System.Text;

namespace Celbridge.Tools;

/// <summary>
/// Concrete IGuides implementation. The static Initialize entry point
/// resolves the singleton from DI and runs Load on it; Load scans embedded
/// markdown under the Celbridge.Tools.Guides.* namespace, validates filenames
/// against the registered MCP tool surface (every per-tool guide must match
/// a registered tool, and every registered tool must have a per-tool guide),
/// and precomputes per-tool invocation strings via reflection. Failures
/// throw from Load so they surface at app startup rather than on the first
/// agent call. Members other than Load throw if used before loading.
/// </summary>
internal sealed class Guides : IGuides
{
    private const string ResourcePrefix = "Celbridge.Tools.Guides.";
    private const string ConceptsSegment = "Concepts.";
    private const string NamespacesSegment = "Namespaces.";
    private const string ToolsSegment = "Tools.";
    private const string MarkdownSuffix = ".md";

    private LoadedState? _state;

    public Guides()
    {
    }

    /// <summary>
    /// Resolves the Guides singleton from DI and loads the embedded guide
    /// set. ServiceConfiguration.Initialize calls this once at app startup;
    /// any embedded-guide validation failure throws here so it fails app
    /// launch rather than the first agent call.
    /// </summary>
    public static void Initialize()
    {
        var guides = ServiceLocator.AcquireService<IGuides>() as Guides;
        Guard.IsNotNull(guides);
        guides.Load();
    }

    /// <summary>
    /// Loads and validates the embedded guide set. Idempotent — subsequent
    /// calls are no-ops. The static Initialize entry point is the production
    /// caller; tests construct a Guides instance and call Load directly.
    /// </summary>
    internal void Load()
    {
        if (_state is not null)
        {
            return;
        }

        var assembly = typeof(Guides).Assembly;
        var rawGuides = LoadRawGuides(assembly);
        var toolAliasNames = DiscoverToolAliasNames(assembly);
        var toolNamespaces = DiscoverToolNamespaces(toolAliasNames);
        var toolInvocations = BuildToolInvocations(assembly);

        var entries = new Dictionary<string, GuideEntry>(StringComparer.Ordinal);

        foreach (var raw in rawGuides)
        {
            ValidateRawGuide(raw, toolAliasNames, toolNamespaces, entries);

            string? pythonInvocation = null;
            string? javaScriptInvocation = null;
            if (raw.Kind == GuideKind.Tool && toolInvocations.TryGetValue(raw.Name, out var pair))
            {
                pythonInvocation = pair.Python;
                javaScriptInvocation = pair.JavaScript;
            }

            var entry = new GuideEntry(
                Name: raw.Name,
                Kind: raw.Kind,
                Body: raw.Body,
                PythonInvocation: pythonInvocation,
                JavaScriptInvocation: javaScriptInvocation);

            entries.Add(raw.Name, entry);
        }

        ValidateEveryToolHasGuide(toolAliasNames, entries);
        ValidateEveryNamespaceHasGuide(toolNamespaces, entries);

        _state = new LoadedState(entries);
    }

    public GuideEntry? GetByName(string name)
    {
        return RequireLoaded().ByName.GetValueOrDefault(name);
    }

    private static void ValidateEveryToolHasGuide(
        HashSet<string> toolAliasNames,
        Dictionary<string, GuideEntry> entries)
    {
        var missing = new List<string>();
        foreach (var toolAliasName in toolAliasNames)
        {
            if (!entries.TryGetValue(toolAliasName, out var entry))
            {
                missing.Add(toolAliasName);
                continue;
            }
            if (entry.Kind != GuideKind.Tool)
            {
                missing.Add(toolAliasName);
            }
        }

        if (missing.Count > 0)
        {
            missing.Sort(StringComparer.Ordinal);
            throw new InvalidDataException(
                "Every registered MCP tool must have a per-tool guide under " +
                "Source/Core/Celbridge.Tools/Guides/Tools/. The following tools are missing a guide: " +
                string.Join(", ", missing) + ".");
        }
    }

    private static void ValidateEveryNamespaceHasGuide(
        HashSet<string> toolNamespaces,
        Dictionary<string, GuideEntry> entries)
    {
        var missing = new List<string>();
        foreach (var namespaceName in toolNamespaces)
        {
            if (!entries.TryGetValue(namespaceName, out var entry))
            {
                missing.Add(namespaceName);
                continue;
            }
            if (entry.Kind != GuideKind.Namespace)
            {
                missing.Add(namespaceName);
            }
        }

        if (missing.Count > 0)
        {
            missing.Sort(StringComparer.Ordinal);
            throw new InvalidDataException(
                "Every registered MCP namespace must have a namespace guide under " +
                "Source/Core/Celbridge.Tools/Guides/Namespaces/. The following namespaces are missing a guide: " +
                string.Join(", ", missing) + ".");
        }
    }

    private static void ValidateRawGuide(
        RawGuide raw,
        HashSet<string> toolAliasNames,
        HashSet<string> toolNamespaces,
        Dictionary<string, GuideEntry> alreadyLoaded)
    {
        if (raw.Kind == GuideKind.Tool)
        {
            if (!toolAliasNames.Contains(raw.Name))
            {
                throw new InvalidDataException(
                    $"Per-tool guide '{raw.Name}' does not match any registered MCP tool alias name.");
            }
        }
        else if (raw.Kind == GuideKind.Namespace)
        {
            if (!toolNamespaces.Contains(raw.Name))
            {
                throw new InvalidDataException(
                    $"Namespace guide '{raw.Name}' does not match any registered MCP tool namespace.");
            }

            if (toolAliasNames.Contains(raw.Name))
            {
                throw new InvalidDataException(
                    $"Namespace guide '{raw.Name}' collides with an MCP tool alias name.");
            }
        }
        else if (toolAliasNames.Contains(raw.Name) || toolNamespaces.Contains(raw.Name))
        {
            throw new InvalidDataException(
                $"Conceptual guide '{raw.Name}' collides with an MCP tool alias or namespace name.");
        }

        if (alreadyLoaded.ContainsKey(raw.Name))
        {
            throw new InvalidDataException(
                $"Guide name '{raw.Name}' is defined in more than one location (or appears twice in one folder).");
        }
    }

    private static List<RawGuide> LoadRawGuides(Assembly assembly)
    {
        var rawGuides = new List<RawGuide>();
        var resourceNames = assembly.GetManifestResourceNames();

        foreach (var resourceName in resourceNames)
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }
            if (!resourceName.EndsWith(MarkdownSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var subPath = resourceName.Substring(ResourcePrefix.Length);
            GuideKind kind;
            string remainder;

            if (subPath.StartsWith(ConceptsSegment, StringComparison.Ordinal))
            {
                kind = GuideKind.Concept;
                remainder = subPath.Substring(ConceptsSegment.Length);
            }
            else if (subPath.StartsWith(NamespacesSegment, StringComparison.Ordinal))
            {
                kind = GuideKind.Namespace;
                remainder = subPath.Substring(NamespacesSegment.Length);
            }
            else if (subPath.StartsWith(ToolsSegment, StringComparison.Ordinal))
            {
                kind = GuideKind.Tool;
                remainder = subPath.Substring(ToolsSegment.Length);
            }
            else
            {
                throw new InvalidDataException(
                    $"Guide resource '{resourceName}' is not under Concepts/, Namespaces/, or Tools/.");
            }

            var guideName = remainder.Substring(0, remainder.Length - MarkdownSuffix.Length);
            if (guideName.Contains('.'))
            {
                throw new InvalidDataException(
                    $"Guide resource '{resourceName}' must live directly under Concepts/, Namespaces/, or Tools/, not in a nested folder.");
            }

            var body = ReadResource(assembly, resourceName);
            rawGuides.Add(new RawGuide(guideName, kind, body));
        }

        return rawGuides;
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Guide resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static HashSet<string> DiscoverToolAliasNames(Assembly assembly)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var xmlReturns = new Dictionary<string, string>();
        var methods = ToolApiReflection.FindToolMethods(assembly, xmlReturns, _ => "");

        foreach (var method in methods)
        {
            names.Add($"{method.Namespace}_{method.MethodName}");
        }

        return names;
    }

    private static HashSet<string> DiscoverToolNamespaces(HashSet<string> toolAliasNames)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var aliasName in toolAliasNames)
        {
            var underscoreIndex = aliasName.IndexOf('_');
            if (underscoreIndex > 0)
            {
                namespaces.Add(aliasName.Substring(0, underscoreIndex));
            }
        }
        return namespaces;
    }

    private static Dictionary<string, (string Python, string JavaScript)> BuildToolInvocations(Assembly assembly)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        var xmlReturns = ToolApiReflection.LoadXmlReturnsDocumentation(assembly);
        var pythonMethods = ToolApiReflection.FindToolMethods(assembly, xmlReturns, BuildPythonSignature);
        var javaScriptMethods = ToolApiReflection.FindToolMethods(assembly, xmlReturns, BuildJavaScriptSignature);

        var javaScriptByKey = new Dictionary<string, ToolMethodInfo>(StringComparer.Ordinal);
        foreach (var method in javaScriptMethods)
        {
            javaScriptByKey[$"{method.Namespace}_{method.MethodName}"] = method;
        }

        foreach (var method in pythonMethods)
        {
            var key = $"{method.Namespace}_{method.MethodName}";
            var python = $"cel.{method.Namespace}.{method.MethodName}({method.ParameterSignature})";
            string javaScript;
            if (javaScriptByKey.TryGetValue(key, out var jsMethod))
            {
                var camelMethod = ConvertSnakeToCamelCase(jsMethod.MethodName);
                javaScript = $"cel.{jsMethod.Namespace}.{camelMethod}({jsMethod.ParameterSignature})";
            }
            else
            {
                javaScript = python;
            }
            result[key] = (python, javaScript);
        }

        return result;
    }

    private static string BuildPythonSignature(MethodInfo method)
    {
        var parts = new List<string>();
        foreach (var parameter in method.GetParameters())
        {
            var name = ConvertCamelToSnakeCase(parameter.Name ?? "");
            var typeName = MapPythonTypeName(parameter.ParameterType);
            parts.Add($"{name}: {typeName}");
        }
        return string.Join(", ", parts);
    }

    private static string BuildJavaScriptSignature(MethodInfo method)
    {
        var parts = new List<string>();
        foreach (var parameter in method.GetParameters())
        {
            var name = ConvertSnakeToCamelCase(parameter.Name ?? "");
            var typeName = MapJavaScriptTypeName(parameter.ParameterType);
            parts.Add($"{name}: {typeName}");
        }
        return string.Join(", ", parts);
    }

    private static string MapPythonTypeName(Type type)
    {
        if (type == typeof(string)) return "str";
        if (type == typeof(int) || type == typeof(long)) return "int";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(double) || type == typeof(float)) return "float";
        return "str";
    }

    private static string MapJavaScriptTypeName(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(int) || type == typeof(long)) return "number";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(double) || type == typeof(float)) return "number";
        return "string";
    }

    private static string ConvertCamelToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 4);
        builder.Append(char.ToLowerInvariant(name[0]));

        for (int index = 1; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character))
            {
                builder.Append('_');
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string ConvertSnakeToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length);
        var upperNext = false;
        for (int index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (character == '_')
            {
                upperNext = true;
                continue;
            }

            if (index == 0)
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

    private LoadedState RequireLoaded()
    {
        return _state
            ?? throw new InvalidOperationException(
                "Guides has not been initialized. Call Initialize before use; the DI container does this once at app startup via Celbridge.Tools.ServiceConfiguration.Initialize.");
    }

    private record class RawGuide(string Name, GuideKind Kind, string Body);

    private record class LoadedState(IReadOnlyDictionary<string, GuideEntry> ByName);
}
