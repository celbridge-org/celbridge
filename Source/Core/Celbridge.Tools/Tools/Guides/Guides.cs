using System.Reflection;
using System.Text;

namespace Celbridge.Tools;

/// <summary>
/// Concrete IGuides implementation. The static Initialize entry point
/// resolves the singleton from DI and runs Load on it; Load scans embedded
/// markdown under the Celbridge.Tools.Guides.* namespace, validates filenames
/// against the registered MCP tool surface (every per-tool guide must match
/// a registered tool, and every registered tool must have a per-tool guide),
/// validates the [RelatedGuides] / troubleshooter wiring, and precomputes
/// per-tool invocation strings via reflection. Failures throw from Load so
/// they surface at app startup rather than on the first agent call. Members
/// other than Load throw if used before loading.
/// </summary>
internal sealed class Guides : IGuides
{
    private const string ResourcePrefix = "Celbridge.Tools.Guides.";
    private const string ConceptsSegment = "Concepts.";
    private const string NamespacesSegment = "Namespaces.";
    private const string ToolsSegment = "Tools.";
    private const string TroubleshootersSegment = "Troubleshooters.";
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
        var guideFiles = LoadGuideFiles(assembly);
        var toolMethods = DiscoverToolMethods(assembly);
        var toolAliasNames = BuildToolAliasNames(toolMethods);
        var toolNamespaces = DiscoverToolNamespaces(toolAliasNames);
        var toolInvocations = BuildToolInvocations(assembly);
        var toolRelatedGuides = BuildToolRelatedGuides(toolMethods);

        var entries = new Dictionary<string, GuideEntry>(StringComparer.Ordinal);

        foreach (var guideFile in guideFiles)
        {
            ValidateGuideFile(guideFile, toolAliasNames, toolNamespaces, entries);

            string? pythonInvocation = null;
            string? javaScriptInvocation = null;
            if (guideFile.Kind == GuideKind.Tool && toolInvocations.TryGetValue(guideFile.Name, out var pair))
            {
                pythonInvocation = pair.Python;
                javaScriptInvocation = pair.JavaScript;
            }

            var entry = new GuideEntry(
                Name: guideFile.Name,
                Kind: guideFile.Kind,
                Body: guideFile.Body,
                PythonInvocation: pythonInvocation,
                JavaScriptInvocation: javaScriptInvocation);

            entries.Add(guideFile.Name, entry);
        }

        ValidateEveryToolHasGuide(toolAliasNames, entries);
        ValidateEveryNamespaceHasGuide(toolNamespaces, entries);
        ValidateRelatedGuidesAttributePresent(toolMethods);
        ValidateRelatedGuideNamesResolve(toolRelatedGuides, entries);
        ValidateConceptGuidesAreReachable(entries, toolRelatedGuides);
        ValidateTroubleshootersWireUpToHelpers(entries);

        _state = new LoadedState(entries, toolRelatedGuides);
    }

    public GuideEntry? GetByName(string name)
    {
        return RequireLoaded().ByName.GetValueOrDefault(name);
    }

    public IReadOnlyList<string> GetRelatedGuides(string toolAliasName)
    {
        var state = RequireLoaded();
        if (state.RelatedGuidesByTool.TryGetValue(toolAliasName, out var related))
        {
            return related;
        }
        return Array.Empty<string>();
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

    private static void ValidateRelatedGuidesAttributePresent(IReadOnlyList<DiscoveredToolMethod> toolMethods)
    {
        var missing = new List<string>();
        foreach (var method in toolMethods)
        {
            if (!method.HasRelatedGuidesAttribute)
            {
                missing.Add(method.AliasName);
            }
        }

        if (missing.Count > 0)
        {
            missing.Sort(StringComparer.Ordinal);
            throw new InvalidDataException(
                "Every MCP tool method must declare a [RelatedGuides] attribute (use [RelatedGuides] " +
                "with no arguments to assert no extra guides). Missing on: " +
                string.Join(", ", missing) + ".");
        }
    }

    private static void ValidateRelatedGuideNamesResolve(
        Dictionary<string, IReadOnlyList<string>> toolRelatedGuides,
        Dictionary<string, GuideEntry> entries)
    {
        var unresolved = new List<string>();
        foreach (var pair in toolRelatedGuides)
        {
            foreach (var relatedName in pair.Value)
            {
                if (!entries.ContainsKey(relatedName))
                {
                    unresolved.Add($"{pair.Key} -> {relatedName}");
                }
            }
        }

        if (unresolved.Count > 0)
        {
            unresolved.Sort(StringComparer.Ordinal);
            throw new InvalidDataException(
                "Every name listed in [RelatedGuides] must resolve to a loaded guide. " +
                "Unresolved references: " + string.Join(", ", unresolved) + ".");
        }
    }

    private static void ValidateConceptGuidesAreReachable(
        Dictionary<string, GuideEntry> entries,
        Dictionary<string, IReadOnlyList<string>> toolRelatedGuides)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var related in toolRelatedGuides.Values)
        {
            foreach (var name in related)
            {
                reachable.Add(name);
            }
        }

        var orphans = new List<string>();
        foreach (var entry in entries.Values)
        {
            if (entry.Kind != GuideKind.Concept)
            {
                continue;
            }
            // The orientation guide is the implicit first candidate for every
            // tool call (see AgentResponseFilter.BuildCandidateList) and is
            // never named in [RelatedGuides], so it is the one concept guide
            // that legitimately has no entry-by-tool reachability.
            if (string.Equals(entry.Name, OrientationGuideName, StringComparison.Ordinal))
            {
                continue;
            }
            if (!reachable.Contains(entry.Name))
            {
                orphans.Add(entry.Name);
            }
        }

        if (orphans.Count > 0)
        {
            orphans.Sort(StringComparer.Ordinal);
            throw new InvalidDataException(
                "Every concept guide under Guides/Concepts/ must appear in at least one tool's " +
                "[RelatedGuides] list. Orphaned concepts: " + string.Join(", ", orphans) +
                ". Re-home into a tool's [RelatedGuides], fold into agent_instructions, or delete.");
        }
    }

    private const string OrientationGuideName = "agent_instructions";

    private static void ValidateTroubleshootersWireUpToHelpers(Dictionary<string, GuideEntry> entries)
    {
        var helperTroubleshooters = ToolResponse.HelperTroubleshooters;
        var declaredNames = new HashSet<string>(helperTroubleshooters.Values, StringComparer.Ordinal);

        var missingFromGuides = new List<string>();
        foreach (var pair in helperTroubleshooters)
        {
            if (!entries.TryGetValue(pair.Value, out var entry)
                || entry.Kind != GuideKind.Troubleshooter)
            {
                missingFromGuides.Add($"{pair.Key} -> {pair.Value}");
            }
        }
        if (missingFromGuides.Count > 0)
        {
            missingFromGuides.Sort(StringComparer.Ordinal);
            throw new InvalidDataException(
                "Every ToolResponse helper that declares a troubleshooter must reference an existing " +
                "troubleshooter guide under Guides/Troubleshooters/. Missing: " +
                string.Join(", ", missingFromGuides) + ".");
        }

        var unreferenced = new List<string>();
        foreach (var entry in entries.Values)
        {
            if (entry.Kind != GuideKind.Troubleshooter)
            {
                continue;
            }
            if (!declaredNames.Contains(entry.Name))
            {
                unreferenced.Add(entry.Name);
            }
        }
        if (unreferenced.Count > 0)
        {
            unreferenced.Sort(StringComparer.Ordinal);
            throw new InvalidDataException(
                "Every troubleshooter guide under Guides/Troubleshooters/ must be referenced by at " +
                "least one ToolResponse helper. Unreferenced troubleshooters: " +
                string.Join(", ", unreferenced) + ".");
        }
    }

    private static void ValidateGuideFile(
        GuideFile guideFile,
        HashSet<string> toolAliasNames,
        HashSet<string> toolNamespaces,
        Dictionary<string, GuideEntry> alreadyLoaded)
    {
        if (guideFile.Kind == GuideKind.Tool)
        {
            if (!toolAliasNames.Contains(guideFile.Name))
            {
                throw new InvalidDataException(
                    $"Per-tool guide '{guideFile.Name}' does not match any registered MCP tool alias name.");
            }
        }
        else if (guideFile.Kind == GuideKind.Namespace)
        {
            if (!toolNamespaces.Contains(guideFile.Name))
            {
                throw new InvalidDataException(
                    $"Namespace guide '{guideFile.Name}' does not match any registered MCP tool namespace.");
            }

            if (toolAliasNames.Contains(guideFile.Name))
            {
                throw new InvalidDataException(
                    $"Namespace guide '{guideFile.Name}' collides with an MCP tool alias name.");
            }
        }
        else if (toolAliasNames.Contains(guideFile.Name) || toolNamespaces.Contains(guideFile.Name))
        {
            // Concept and troubleshooter guides must not collide with the tool or namespace surface.
            throw new InvalidDataException(
                $"{guideFile.Kind} guide '{guideFile.Name}' collides with an MCP tool alias or namespace name.");
        }

        if (alreadyLoaded.ContainsKey(guideFile.Name))
        {
            throw new InvalidDataException(
                $"Guide name '{guideFile.Name}' is defined in more than one location (or appears twice in one folder).");
        }
    }

    private static List<GuideFile> LoadGuideFiles(Assembly assembly)
    {
        var guideFiles = new List<GuideFile>();
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
            else if (subPath.StartsWith(TroubleshootersSegment, StringComparison.Ordinal))
            {
                kind = GuideKind.Troubleshooter;
                remainder = subPath.Substring(TroubleshootersSegment.Length);
            }
            else
            {
                throw new InvalidDataException(
                    $"Guide resource '{resourceName}' is not under Concepts/, Namespaces/, Tools/, or Troubleshooters/.");
            }

            var guideName = remainder.Substring(0, remainder.Length - MarkdownSuffix.Length);
            if (guideName.Contains('.'))
            {
                throw new InvalidDataException(
                    $"Guide resource '{resourceName}' must live directly under its kind folder, not in a nested folder.");
            }

            var body = ReadResource(assembly, resourceName);
            guideFiles.Add(new GuideFile(guideName, kind, body));
        }

        return guideFiles;
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Guide resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static List<DiscoveredToolMethod> DiscoverToolMethods(Assembly assembly)
    {
        var xmlReturns = new Dictionary<string, string>();
        var methods = ToolApiReflection.FindToolMethods(assembly, xmlReturns, _ => "");

        var results = new List<DiscoveredToolMethod>(methods.Count);
        foreach (var method in methods)
        {
            var aliasName = $"{method.Namespace}_{method.MethodName}";
            results.Add(new DiscoveredToolMethod(
                AliasName: aliasName,
                HasRelatedGuidesAttribute: method.HasRelatedGuidesAttribute,
                RelatedGuides: method.RelatedGuides));
        }
        return results;
    }

    private static HashSet<string> BuildToolAliasNames(IReadOnlyList<DiscoveredToolMethod> toolMethods)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in toolMethods)
        {
            names.Add(method.AliasName);
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

    private static Dictionary<string, IReadOnlyList<string>> BuildToolRelatedGuides(
        IReadOnlyList<DiscoveredToolMethod> toolMethods)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var method in toolMethods)
        {
            result[method.AliasName] = method.RelatedGuides;
        }
        return result;
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

    private record class GuideFile(string Name, GuideKind Kind, string Body);

    private record class DiscoveredToolMethod(
        string AliasName,
        bool HasRelatedGuidesAttribute,
        IReadOnlyList<string> RelatedGuides);

    private record class LoadedState(
        IReadOnlyDictionary<string, GuideEntry> ByName,
        IReadOnlyDictionary<string, IReadOnlyList<string>> RelatedGuidesByTool);
}
