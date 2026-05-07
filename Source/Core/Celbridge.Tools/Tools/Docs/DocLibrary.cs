using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Celbridge.Tools;

/// <summary>
/// Concrete IDocLibrary implementation. The static Initialize entry point
/// resolves the singleton from DI and runs Load on it; Load scans embedded
/// markdown under the Celbridge.Tools.Docs.* namespace, parses frontmatter,
/// validates names against the registered MCP tool surface, and precomputes
/// per-tool invocation strings via reflection. Failures throw from Load so
/// they surface at app startup rather than on the first agent call. Members
/// other than Load throw if used before loading.
/// </summary>
internal sealed class DocLibrary : IDocLibrary
{
    private const int DefaultPriority = 100;
    private const string ResourcePrefix = "Celbridge.Tools.Docs.";
    private const string ConceptsSegment = "Concepts.";
    private const string ToolsSegment = "Tools.";
    private const string MarkdownSuffix = ".md";

    private static readonly TimeSpan SearchRegexTimeout = TimeSpan.FromMilliseconds(300);

    private LoadedState? _state;

    public DocLibrary()
    {
    }

    /// <summary>
    /// Resolves the DocLibrary singleton from DI and loads the embedded doc
    /// set. ServiceConfiguration.Initialize calls this once at app startup;
    /// any embedded-doc validation failure throws here so it fails app
    /// launch rather than the first agent call.
    /// </summary>
    public static void Initialize()
    {
        var docLibrary = ServiceLocator.AcquireService<IDocLibrary>() as DocLibrary;
        Guard.IsNotNull(docLibrary);
        docLibrary.Load();
    }

    /// <summary>
    /// Loads and validates the embedded doc set. Idempotent — subsequent
    /// calls are no-ops. The static Initialize entry point is the production
    /// caller; tests construct a DocLibrary and call Load directly.
    /// </summary>
    internal void Load()
    {
        if (_state is not null)
        {
            return;
        }

        var assembly = typeof(DocLibrary).Assembly;
        var rawDocs = LoadRawDocs(assembly);
        var toolAliasNames = DiscoverToolAliasNames(assembly);
        var toolInvocations = BuildToolInvocations(assembly);

        var entries = new Dictionary<string, DocEntry>(StringComparer.Ordinal);

        foreach (var raw in rawDocs)
        {
            ValidateRawDoc(raw, toolAliasNames, entries);

            string? pythonInvocation = null;
            string? javaScriptInvocation = null;
            if (raw.Kind == DocKind.Tool && toolInvocations.TryGetValue(raw.Name, out var pair))
            {
                pythonInvocation = pair.Python;
                javaScriptInvocation = pair.JavaScript;
            }

            var entry = new DocEntry(
                Name: raw.Name,
                Kind: raw.Kind,
                Description: raw.Frontmatter.Description,
                Priority: raw.Frontmatter.Priority ?? DefaultPriority,
                Body: raw.Body,
                PythonInvocation: pythonInvocation,
                JavaScriptInvocation: javaScriptInvocation);

            entries.Add(raw.Name, entry);
        }

        var sortedIndex = entries.Values
            .OrderBy(e => e.Kind == DocKind.Concept ? 0 : 1)
            .ThenBy(e => e.Kind == DocKind.Concept ? e.Priority : 0)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();

        _state = new LoadedState(entries, sortedIndex, toolInvocations);
    }

    public IReadOnlyList<DocEntry> Index => RequireLoaded().SortedIndex;

    public DocEntry? GetByName(string name)
    {
        return RequireLoaded().ByName.GetValueOrDefault(name);
    }

    public (string PythonInvocation, string JavaScriptInvocation)? GetToolInvocations(string toolAliasName)
    {
        return RequireLoaded().ToolInvocations.TryGetValue(toolAliasName, out var pair) ? pair : null;
    }

    public bool IsKnownToolAliasName(string toolAliasName)
    {
        return RequireLoaded().ToolInvocations.ContainsKey(toolAliasName);
    }

    public IReadOnlyList<DocSearchMatch> Search(string pattern, out string? errorMessage)
    {
        var state = RequireLoaded();
        errorMessage = null;

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, SearchRegexTimeout);
        }
        catch (ArgumentException exception)
        {
            errorMessage = exception.Message;
            return Array.Empty<DocSearchMatch>();
        }

        var matches = new List<DocSearchMatch>();
        foreach (var doc in state.SortedIndex)
        {
            DocSearchMatch? match = null;
            try
            {
                match = ScoreDoc(regex, doc);
            }
            catch (RegexMatchTimeoutException exception)
            {
                errorMessage = $"Search timed out after {SearchRegexTimeout.TotalMilliseconds:F0}ms: {exception.Message}";
                return Array.Empty<DocSearchMatch>();
            }

            if (match is not null)
            {
                matches.Add(match);
            }
        }

        matches.Sort((a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0)
            {
                return byScore;
            }
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
        });

        return matches;
    }

    private static DocSearchMatch? ScoreDoc(Regex regex, DocEntry doc)
    {
        var nameMatchCount = regex.Matches(doc.Name).Count;
        var descriptionMatchCount = regex.Matches(doc.Description).Count;
        var bodyMatchCollection = regex.Matches(doc.Body);
        var bodyMatchCount = bodyMatchCollection.Count;

        if (nameMatchCount == 0 && descriptionMatchCount == 0 && bodyMatchCount == 0)
        {
            return null;
        }

        var bodyLength = Math.Max(doc.Body.Length, 1);
        var nameWeight = 100.0 * nameMatchCount;
        var descriptionWeight = 25.0 * descriptionMatchCount;
        var bodyWeight = 1000.0 * bodyMatchCount / bodyLength;
        var score = nameWeight + descriptionWeight + bodyWeight;

        var snippet = BuildSnippet(doc, bodyMatchCollection, regex);
        return new DocSearchMatch(doc.Name, doc.Kind, doc.Description, snippet, score);
    }

    private static string BuildSnippet(DocEntry doc, MatchCollection bodyMatches, Regex regex)
    {
        const int snippetWindow = 80;
        const int snippetMaxLength = 200;

        Match? primaryMatch = null;
        if (bodyMatches.Count > 0)
        {
            primaryMatch = bodyMatches[0];
        }

        string source;
        int matchStart;
        int matchLength;

        if (primaryMatch is not null)
        {
            source = doc.Body;
            matchStart = primaryMatch.Index;
            matchLength = primaryMatch.Length;
        }
        else
        {
            var descriptionMatch = regex.Match(doc.Description);
            if (descriptionMatch.Success)
            {
                source = doc.Description;
                matchStart = descriptionMatch.Index;
                matchLength = descriptionMatch.Length;
            }
            else
            {
                return TruncateSnippet(doc.Description, snippetMaxLength);
            }
        }

        var snippetStart = Math.Max(0, matchStart - snippetWindow);
        var snippetEnd = Math.Min(source.Length, matchStart + matchLength + snippetWindow);
        var prefix = snippetStart > 0 ? "..." : "";
        var suffix = snippetEnd < source.Length ? "..." : "";

        var before = source.Substring(snippetStart, matchStart - snippetStart);
        var matched = source.Substring(matchStart, matchLength);
        var after = source.Substring(matchStart + matchLength, snippetEnd - matchStart - matchLength);

        var snippet = $"{prefix}{before}**{matched}**{after}{suffix}".Replace('\n', ' ').Replace('\r', ' ');
        return TruncateSnippet(snippet, snippetMaxLength);
    }

    private static string TruncateSnippet(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }
        return text.Substring(0, maxLength - 3) + "...";
    }

    private static void ValidateRawDoc(
        RawDoc raw,
        HashSet<string> toolAliasNames,
        Dictionary<string, DocEntry> alreadyLoaded)
    {
        if (raw.Kind == DocKind.Tool)
        {
            if (raw.Frontmatter.Priority.HasValue)
            {
                throw new InvalidDataException(
                    $"Per-tool doc '{raw.Name}' sets 'priority' in frontmatter; priority is only valid on conceptual docs.");
            }

            if (!toolAliasNames.Contains(raw.Name))
            {
                throw new InvalidDataException(
                    $"Per-tool doc '{raw.Name}' does not match any registered MCP tool alias name.");
            }
        }
        else if (toolAliasNames.Contains(raw.Name))
        {
            throw new InvalidDataException(
                $"Conceptual doc '{raw.Name}' collides with an MCP tool alias name.");
        }

        if (alreadyLoaded.ContainsKey(raw.Name))
        {
            throw new InvalidDataException(
                $"Doc name '{raw.Name}' is defined in both Concepts/ and Tools/ (or appears twice in one folder).");
        }
    }

    private static List<RawDoc> LoadRawDocs(Assembly assembly)
    {
        var rawDocs = new List<RawDoc>();
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
            DocKind kind;
            string remainder;

            if (subPath.StartsWith(ConceptsSegment, StringComparison.Ordinal))
            {
                kind = DocKind.Concept;
                remainder = subPath.Substring(ConceptsSegment.Length);
            }
            else if (subPath.StartsWith(ToolsSegment, StringComparison.Ordinal))
            {
                kind = DocKind.Tool;
                remainder = subPath.Substring(ToolsSegment.Length);
            }
            else
            {
                throw new InvalidDataException(
                    $"Doc resource '{resourceName}' is not under Concepts/ or Tools/.");
            }

            var docName = remainder.Substring(0, remainder.Length - MarkdownSuffix.Length);
            if (docName.Contains('.'))
            {
                throw new InvalidDataException(
                    $"Doc resource '{resourceName}' must live directly under Concepts/ or Tools/, not in a nested folder.");
            }

            var content = ReadResource(assembly, resourceName);
            var (frontmatter, body) = ParseFrontmatter(content, resourceName);

            if (frontmatter.Name != docName)
            {
                throw new InvalidDataException(
                    $"Doc '{resourceName}' frontmatter name '{frontmatter.Name}' does not match its file name '{docName}'.");
            }

            rawDocs.Add(new RawDoc(docName, kind, frontmatter, body));
        }

        return rawDocs;
    }

    private static string ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Doc resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static (Frontmatter Frontmatter, string Body) ParseFrontmatter(string content, string resourceName)
    {
        var normalised = content.Replace("\r\n", "\n");
        if (!normalised.StartsWith("---\n", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Doc '{resourceName}' is missing the opening '---' frontmatter line.");
        }

        var closing = normalised.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (closing < 0)
        {
            throw new InvalidDataException($"Doc '{resourceName}' is missing the closing '---' frontmatter line.");
        }

        var frontmatterText = normalised.Substring(4, closing - 4);
        var body = normalised.Substring(closing + 5);

        string? name = null;
        string? description = null;
        int? priority = null;

        foreach (var rawLine in frontmatterText.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                throw new InvalidDataException($"Doc '{resourceName}' frontmatter line '{line}' is not 'key: value'.");
            }

            var key = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim();
            value = StripSurroundingQuotes(value);

            switch (key)
            {
                case "name":
                    name = value;
                    break;
                case "description":
                    description = value;
                    break;
                case "priority":
                    if (!int.TryParse(value, out var parsedPriority))
                    {
                        throw new InvalidDataException($"Doc '{resourceName}' has non-integer priority '{value}'.");
                    }
                    priority = parsedPriority;
                    break;
                default:
                    throw new InvalidDataException($"Doc '{resourceName}' has unknown frontmatter key '{key}'.");
            }
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidDataException($"Doc '{resourceName}' frontmatter is missing required field 'name'.");
        }
        if (string.IsNullOrEmpty(description))
        {
            throw new InvalidDataException($"Doc '{resourceName}' frontmatter is missing required field 'description'.");
        }

        return (new Frontmatter(name, description, priority), body);
    }

    private static string StripSurroundingQuotes(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            {
                return value.Substring(1, value.Length - 2);
            }
        }
        return value;
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
                "DocLibrary has not been initialized. Call Initialize before use; the DI container does this once at app startup via Celbridge.Tools.ServiceConfiguration.Initialize.");
    }

    private record class Frontmatter(string Name, string Description, int? Priority);

    private record class RawDoc(string Name, DocKind Kind, Frontmatter Frontmatter, string Body);

    private record class LoadedState(
        IReadOnlyDictionary<string, DocEntry> ByName,
        IReadOnlyList<DocEntry> SortedIndex,
        IReadOnlyDictionary<string, (string Python, string JavaScript)> ToolInvocations);
}
