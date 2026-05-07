using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Celbridge.Tools;

/// <summary>
/// Concrete IGuides implementation. The static Initialize entry point
/// resolves the singleton from DI and runs Load on it; Load scans embedded
/// markdown under the Celbridge.Tools.Guides.* namespace, parses frontmatter,
/// validates names against the registered MCP tool surface, and precomputes
/// per-tool invocation strings via reflection. Failures throw from Load so
/// they surface at app startup rather than on the first agent call. Members
/// other than Load throw if used before loading.
/// </summary>
internal sealed class Guides : IGuides
{
    private const int DefaultPriority = 100;
    private const string ResourcePrefix = "Celbridge.Tools.Guides.";
    private const string ConceptsSegment = "Concepts.";
    private const string ToolsSegment = "Tools.";
    private const string MarkdownSuffix = ".md";

    private static readonly TimeSpan SearchRegexTimeout = TimeSpan.FromMilliseconds(300);

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
        var toolInvocations = BuildToolInvocations(assembly);

        var entries = new Dictionary<string, GuideEntry>(StringComparer.Ordinal);

        foreach (var raw in rawGuides)
        {
            ValidateRawGuide(raw, toolAliasNames, entries);

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
                Description: raw.Frontmatter.Description,
                Priority: raw.Frontmatter.Priority ?? DefaultPriority,
                Body: raw.Body,
                PythonInvocation: pythonInvocation,
                JavaScriptInvocation: javaScriptInvocation);

            entries.Add(raw.Name, entry);
        }

        var sortedIndex = entries.Values
            .OrderBy(e => e.Kind == GuideKind.Concept ? 0 : 1)
            .ThenBy(e => e.Kind == GuideKind.Concept ? e.Priority : 0)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();

        _state = new LoadedState(entries, sortedIndex, toolInvocations);
    }

    public IReadOnlyList<GuideEntry> Index => RequireLoaded().SortedIndex;

    public GuideEntry? GetByName(string name)
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

    public IReadOnlyList<GuideSearchMatch> Search(string pattern, out string? errorMessage)
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
            return Array.Empty<GuideSearchMatch>();
        }

        var matches = new List<GuideSearchMatch>();
        foreach (var guide in state.SortedIndex)
        {
            GuideSearchMatch? match = null;
            try
            {
                match = ScoreGuide(regex, guide);
            }
            catch (RegexMatchTimeoutException exception)
            {
                errorMessage = $"Search timed out after {SearchRegexTimeout.TotalMilliseconds:F0}ms: {exception.Message}";
                return Array.Empty<GuideSearchMatch>();
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

    private static GuideSearchMatch? ScoreGuide(Regex regex, GuideEntry guide)
    {
        var nameMatchCount = regex.Matches(guide.Name).Count;
        var descriptionMatchCount = regex.Matches(guide.Description).Count;
        var bodyMatchCollection = regex.Matches(guide.Body);
        var bodyMatchCount = bodyMatchCollection.Count;

        if (nameMatchCount == 0 && descriptionMatchCount == 0 && bodyMatchCount == 0)
        {
            return null;
        }

        var bodyLength = Math.Max(guide.Body.Length, 1);
        var nameWeight = 100.0 * nameMatchCount;
        var descriptionWeight = 25.0 * descriptionMatchCount;
        var bodyWeight = 1000.0 * bodyMatchCount / bodyLength;
        var score = nameWeight + descriptionWeight + bodyWeight;

        var snippet = BuildSnippet(guide, bodyMatchCollection, regex);
        return new GuideSearchMatch(guide.Name, guide.Kind, guide.Description, snippet, score);
    }

    private static string BuildSnippet(GuideEntry guide, MatchCollection bodyMatches, Regex regex)
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
            source = guide.Body;
            matchStart = primaryMatch.Index;
            matchLength = primaryMatch.Length;
        }
        else
        {
            var descriptionMatch = regex.Match(guide.Description);
            if (descriptionMatch.Success)
            {
                source = guide.Description;
                matchStart = descriptionMatch.Index;
                matchLength = descriptionMatch.Length;
            }
            else
            {
                return TruncateSnippet(guide.Description, snippetMaxLength);
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

    private static void ValidateRawGuide(
        RawGuide raw,
        HashSet<string> toolAliasNames,
        Dictionary<string, GuideEntry> alreadyLoaded)
    {
        if (raw.Kind == GuideKind.Tool)
        {
            if (raw.Frontmatter.Priority.HasValue)
            {
                throw new InvalidDataException(
                    $"Per-tool guide '{raw.Name}' sets 'priority' in frontmatter; priority is only valid on conceptual guides.");
            }

            if (!toolAliasNames.Contains(raw.Name))
            {
                throw new InvalidDataException(
                    $"Per-tool guide '{raw.Name}' does not match any registered MCP tool alias name.");
            }
        }
        else if (toolAliasNames.Contains(raw.Name))
        {
            throw new InvalidDataException(
                $"Conceptual guide '{raw.Name}' collides with an MCP tool alias name.");
        }

        if (alreadyLoaded.ContainsKey(raw.Name))
        {
            throw new InvalidDataException(
                $"Guide name '{raw.Name}' is defined in both Concepts/ and Tools/ (or appears twice in one folder).");
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
            else if (subPath.StartsWith(ToolsSegment, StringComparison.Ordinal))
            {
                kind = GuideKind.Tool;
                remainder = subPath.Substring(ToolsSegment.Length);
            }
            else
            {
                throw new InvalidDataException(
                    $"Guide resource '{resourceName}' is not under Concepts/ or Tools/.");
            }

            var guideName = remainder.Substring(0, remainder.Length - MarkdownSuffix.Length);
            if (guideName.Contains('.'))
            {
                throw new InvalidDataException(
                    $"Guide resource '{resourceName}' must live directly under Concepts/ or Tools/, not in a nested folder.");
            }

            var content = ReadResource(assembly, resourceName);
            var (frontmatter, body) = ParseFrontmatter(content, resourceName);

            if (frontmatter.Name != guideName)
            {
                throw new InvalidDataException(
                    $"Guide '{resourceName}' frontmatter name '{frontmatter.Name}' does not match its file name '{guideName}'.");
            }

            rawGuides.Add(new RawGuide(guideName, kind, frontmatter, body));
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

    private static (Frontmatter Frontmatter, string Body) ParseFrontmatter(string content, string resourceName)
    {
        var normalised = content.Replace("\r\n", "\n");
        if (!normalised.StartsWith("---\n", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Guide '{resourceName}' is missing the opening '---' frontmatter line.");
        }

        var closing = normalised.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (closing < 0)
        {
            throw new InvalidDataException($"Guide '{resourceName}' is missing the closing '---' frontmatter line.");
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
                throw new InvalidDataException($"Guide '{resourceName}' frontmatter line '{line}' is not 'key: value'.");
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
                        throw new InvalidDataException($"Guide '{resourceName}' has non-integer priority '{value}'.");
                    }
                    priority = parsedPriority;
                    break;
                default:
                    throw new InvalidDataException($"Guide '{resourceName}' has unknown frontmatter key '{key}'.");
            }
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new InvalidDataException($"Guide '{resourceName}' frontmatter is missing required field 'name'.");
        }
        if (string.IsNullOrEmpty(description))
        {
            throw new InvalidDataException($"Guide '{resourceName}' frontmatter is missing required field 'description'.");
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
                "Guides has not been initialized. Call Initialize before use; the DI container does this once at app startup via Celbridge.Tools.ServiceConfiguration.Initialize.");
    }

    private record class Frontmatter(string Name, string Description, int? Priority);

    private record class RawGuide(string Name, GuideKind Kind, Frontmatter Frontmatter, string Body);

    private record class LoadedState(
        IReadOnlyDictionary<string, GuideEntry> ByName,
        IReadOnlyList<GuideEntry> SortedIndex,
        IReadOnlyDictionary<string, (string Python, string JavaScript)> ToolInvocations);
}
