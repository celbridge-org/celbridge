using System.Text.RegularExpressions;

namespace Celbridge.Tests.Architecture;

/// <summary>
/// Guards the host and web content JSON-RPC contract. The two sides are written and tested independently, so a
/// method one side stops honouring leaves no trace at runtime: an unimplemented host method has a default
/// interface body that accepts the call and does nothing, and a method the host never declares is dropped as
/// expected control flow. These tests compare the two sides, so a half-wired method fails the build rather
/// than failing silently in a package author's editor. A method that is not honoured yet has no way to be
/// excused: wire it up or delete it.
/// </summary>
[TestFixture]
public class WebChannelContractTests
{
    // A wire name: a lower camel namespace, a slash, and a lower camel method.
    private const string WireNamePattern = @"[a-z][A-Za-z]*/[a-z][A-Za-z]*";

    [Test]
    public void EveryMethodTheWebClientUsesIsDeclaredByTheHost()
    {
        var declaredMethods = CollectDeclaredMethods().Values.ToHashSet(StringComparer.Ordinal);
        var webUsages = CollectWebRpcCalls();

        var undeclared = webUsages.Keys
            .Where(method => !declaredMethods.Contains(method))
            .Select(method => $"{method} (used by {webUsages[method]})")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        string.Join(Environment.NewLine, undeclared).Should().BeEmpty(
            "every method name the web client sends or listens for must be declared in an RpcMethods class, so the two sides cannot drift apart");
    }

    [Test]
    public void EveryMethodTheHostReceivesHasAnImplementation()
    {
        var declaredMethods = CollectDeclaredMethods();
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        var productionFiles = ArchitectureHelpers.EnumerateProductionSourceFiles(sourceFolder).ToList();

        var unimplemented = new List<string>();
        foreach (var declaration in CollectHostReceivedMethods(declaredMethods))
        {
            // A member declared without a body must be implemented for the solution to compile. Only a
            // default body can be left unimplemented, and that is the case that fails silently.
            if (!declaration.HasDefaultBody)
            {
                continue;
            }

            var isImplemented = productionFiles
                .Where(filePath => !string.Equals(filePath, declaration.DeclaringFile, StringComparison.OrdinalIgnoreCase))
                .Any(filePath => Regex.IsMatch(File.ReadAllText(filePath), $@"\b{Regex.Escape(declaration.MemberName)}\s*\("));

            if (!isImplemented)
            {
                unimplemented.Add($"{declaration.WireName} (declared as {declaration.MemberName}, no implementer)");
            }
        }

        unimplemented.Sort(StringComparer.Ordinal);
        string.Join(Environment.NewLine, unimplemented).Should().BeEmpty(
            "a method the web client can call must do something; a default interface body accepts the call and silently discards it");
    }

    [Test]
    public void EveryMethodTheHostSendsHasAWebHandler()
    {
        var declaredMethods = CollectDeclaredMethods();
        var webMentions = CollectWebMentions();

        var unhandled = CollectHostSentMethods(declaredMethods)
            .Where(method => !webMentions.Contains(method))
            .OrderBy(method => method, StringComparer.Ordinal)
            .ToList();

        string.Join(Environment.NewLine, unhandled).Should().BeEmpty(
            "a notification the host sends must have a web handler listening for that exact name, or it goes nowhere");
    }

    // Wire names declared in the RpcMethods classes, keyed by the qualified constant reference the call sites
    // use (for example InputRpcMethods.OpenResource).
    private static Dictionary<string, string> CollectDeclaredMethods()
    {
        var declaredMethods = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();

        foreach (var filePath in ArchitectureHelpers.EnumerateProductionSourceFiles(sourceFolder))
        {
            var contents = File.ReadAllText(filePath);
            if (!contents.Contains("RpcMethods"))
            {
                continue;
            }

            var className = string.Empty;
            foreach (var line in contents.Split('\n'))
            {
                var classMatch = Regex.Match(line, @"class\s+(\w*RpcMethods)\b");
                if (classMatch.Success)
                {
                    className = classMatch.Groups[1].Value;
                    continue;
                }

                if (className.Length == 0)
                {
                    continue;
                }

                var constantMatch = Regex.Match(line, $@"const\s+string\s+(\w+)\s*=\s*""({WireNamePattern})""");
                if (constantMatch.Success)
                {
                    var reference = $"{className}.{constantMatch.Groups[1].Value}";
                    declaredMethods[reference] = constantMatch.Groups[2].Value;
                }
            }
        }

        return declaredMethods;
    }

    // Methods the host exposes to web content, from their [JsonRpcMethod] declarations.
    private static List<HostMethodDeclaration> CollectHostReceivedMethods(Dictionary<string, string> declaredMethods)
    {
        var declarations = new List<HostMethodDeclaration>();
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();

        foreach (var filePath in ArchitectureHelpers.EnumerateProductionSourceFiles(sourceFolder))
        {
            var contents = File.ReadAllText(filePath);
            if (!contents.Contains("[JsonRpcMethod("))
            {
                continue;
            }

            var pattern = @"\[JsonRpcMethod\(([\w.]+)\)\][\s\S]*?[\w<>?\[\], ]+?\s+(\w+)\s*\([^)]*\)\s*(;|\{\s*\})";
            foreach (Match match in Regex.Matches(contents, pattern))
            {
                if (!declaredMethods.TryGetValue(match.Groups[1].Value, out var wireName))
                {
                    continue;
                }

                var declaration = new HostMethodDeclaration(
                    wireName,
                    match.Groups[2].Value,
                    filePath,
                    match.Groups[3].Value != ";");

                declarations.Add(declaration);
            }
        }

        return declarations;
    }

    // Wire names the host sends to web content, by constant reference or as a literal at the call site.
    private static HashSet<string> CollectHostSentMethods(Dictionary<string, string> declaredMethods)
    {
        var sentMethods = new HashSet<string>(StringComparer.Ordinal);
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        var pattern = $@"(?:NotifyAsync|NotifyWithParameterObjectAsync|InvokeAsync)(?:<[^>]*>)?\s*\(\s*(?:""({WireNamePattern})""|([\w.]+))";

        foreach (var filePath in ArchitectureHelpers.EnumerateProductionSourceFiles(sourceFolder))
        {
            var contents = File.ReadAllText(filePath);
            foreach (Match match in Regex.Matches(contents, pattern))
            {
                if (match.Groups[1].Success)
                {
                    sentMethods.Add(match.Groups[1].Value);
                    continue;
                }

                if (declaredMethods.TryGetValue(match.Groups[2].Value, out var wireName))
                {
                    sentMethods.Add(wireName);
                }
            }
        }

        return sentMethods;
    }

    // Wire names the first-party web code calls or registers a handler for, mapped to the file they appear
    // in. Matched by call shape, so a name-shaped string that is not an RPC method (a MIME type, an import
    // path) is not mistaken for one.
    private static Dictionary<string, string> CollectWebRpcCalls()
    {
        var calls = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        var pattern = $@"\.(?:notify|request|addEventListener|onNotification|onRequest|setRequestHandler)\(\s*'({WireNamePattern})'";

        foreach (var filePath in ArchitectureHelpers.EnumerateProductionWebFiles(sourceFolder))
        {
            var contents = File.ReadAllText(filePath);
            foreach (Match match in Regex.Matches(contents, pattern))
            {
                calls[match.Groups[1].Value] = Path.GetFileName(filePath);
            }
        }

        return calls;
    }

    // Every wire name the first-party web code mentions at all. Deliberately looser than the call scan: a
    // handler can be registered through a shape this test does not know (the state stores pass the name to a
    // constructor), and for "does anything over there listen for this" a mention is evidence enough.
    private static HashSet<string> CollectWebMentions()
    {
        var mentions = new HashSet<string>(StringComparer.Ordinal);
        var sourceFolder = ArchitectureHelpers.FindSourceFolder();
        var pattern = $@"['""]({WireNamePattern})['""]";

        foreach (var filePath in ArchitectureHelpers.EnumerateProductionWebFiles(sourceFolder))
        {
            var contents = File.ReadAllText(filePath);
            foreach (Match match in Regex.Matches(contents, pattern))
            {
                mentions.Add(match.Groups[1].Value);
            }
        }

        return mentions;
    }

    private sealed record HostMethodDeclaration(
        string WireName,
        string MemberName,
        string DeclaringFile,
        bool HasDefaultBody);
}
