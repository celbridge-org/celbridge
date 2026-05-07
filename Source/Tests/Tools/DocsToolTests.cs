using System.Text.Json;
using Celbridge.Server;
using Celbridge.Tools;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the DocsTools MCP tool methods. Also exercises the embedded
/// doc library loader: any missing frontmatter, name collision, or per-tool
/// doc whose name doesn't match a registered tool surfaces here rather than
/// on first agent call.
/// </summary>
[TestFixture]
public class DocsToolTests
{
    // Hand-rolled stub instead of substituting IApplicationServiceProvider
    // wholesale, so DocsTools can resolve the internal IDocLibrary without
    // forcing Castle DynamicProxy to generate a proxy for it (which would
    // require an InternalsVisibleTo("DynamicProxyGenAssembly2") entry on
    // Celbridge.Tools). Other services (e.g. ICommandService, resolved
    // eagerly by AgentToolBase) are public interfaces and fall through to
    // NSubstitute — the docs tools never call them, so a no-op auto-mock is
    // fine.
    private sealed class TestServiceProvider : IApplicationServiceProvider
    {
        public IDocLibrary DocLibrary { get; }

        public TestServiceProvider()
        {
            var library = new DocLibrary();
            library.Load();
            DocLibrary = library;
        }

        public T GetRequiredService<T>() where T : class
        {
            if (DocLibrary is T docLibrary)
            {
                return docLibrary;
            }
            return Substitute.For<T>();
        }
    }

    private TestServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _services = new TestServiceProvider();
    }

    [Test]
    public void List_ReturnsConceptDocsBeforeToolDocs()
    {
        var tools = new DocsTools(_services);
        var json = GetResultText(tools.List());
        var root = JsonDocument.Parse(json).RootElement;
        var docs = root.GetProperty("docs");

        docs.GetArrayLength().Should().BeGreaterThan(0);

        var lastConceptIndex = -1;
        var firstToolIndex = int.MaxValue;
        for (int index = 0; index < docs.GetArrayLength(); index++)
        {
            var kind = docs[index].GetProperty("kind").GetString();
            if (kind == "concept")
            {
                lastConceptIndex = index;
            }
            else if (kind == "tool" && firstToolIndex == int.MaxValue)
            {
                firstToolIndex = index;
            }
        }

        if (firstToolIndex != int.MaxValue)
        {
            lastConceptIndex.Should().BeLessThan(firstToolIndex);
        }
    }

    [Test]
    public void List_StartsWithGettingStarted()
    {
        var tools = new DocsTools(_services);
        var json = GetResultText(tools.List());
        var root = JsonDocument.Parse(json).RootElement;
        var firstEntry = root.GetProperty("docs")[0];

        firstEntry.GetProperty("name").GetString().Should().Be("getting_started");
        firstEntry.GetProperty("kind").GetString().Should().Be("concept");
    }

    [Test]
    public void Read_ResolvesConceptDoc()
    {
        var tools = new DocsTools(_services);
        var json = GetResultText(tools.Read("[\"resource_keys\"]"));
        var root = JsonDocument.Parse(json).RootElement;
        var results = root.GetProperty("results");

        results.GetArrayLength().Should().Be(1);
        results[0].GetProperty("name").GetString().Should().Be("resource_keys");
        results[0].GetProperty("kind").GetString().Should().Be("concept");
        results[0].GetProperty("body").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("unknown").GetArrayLength().Should().Be(0);
    }

    [Test]
    public void Read_UnknownNameLandsInUnknown()
    {
        var tools = new DocsTools(_services);
        var json = GetResultText(tools.Read("[\"definitely_not_a_real_doc\"]"));
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("results").GetArrayLength().Should().Be(0);
        var unknown = root.GetProperty("unknown");
        unknown.GetArrayLength().Should().Be(1);
        unknown[0].GetString().Should().Be("definitely_not_a_real_doc");
    }

    [Test]
    public void Read_ToolAliasNameWithoutPerToolDocReturnsStubWithInvocations()
    {
        var tools = new DocsTools(_services);
        var json = GetResultText(tools.Read("[\"file_grep\"]"));
        var root = JsonDocument.Parse(json).RootElement;
        var results = root.GetProperty("results");

        results.GetArrayLength().Should().Be(1);
        var entry = results[0];
        entry.GetProperty("kind").GetString().Should().Be("tool");
        entry.GetProperty("pythonInvocation").GetString().Should().StartWith("cel.file.grep(");
        entry.GetProperty("javaScriptInvocation").GetString().Should().StartWith("cel.file.grep(");
    }

    [Test]
    public void Search_FindsResourceKeysDoc()
    {
        var tools = new DocsTools(_services);
        var json = GetResultText(tools.Search("resource keys", 10));
        var root = JsonDocument.Parse(json).RootElement;
        var matches = root.GetProperty("matches");

        var matchNames = new List<string>();
        for (int index = 0; index < matches.GetArrayLength(); index++)
        {
            matchNames.Add(matches[index].GetProperty("name").GetString()!);
        }
        matchNames.Should().Contain("resource_keys");
        root.GetProperty("totalMatches").GetInt32().Should().BeGreaterThan(0);
    }

    [Test]
    public void Search_ReturnsErrorOnInvalidRegex()
    {
        var tools = new DocsTools(_services);
        var json = GetResultText(tools.Search("[unclosed", 10));
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("matches").GetArrayLength().Should().Be(0);
        root.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public void Search_ClampsLimitAboveMax()
    {
        var tools = new DocsTools(_services);
        var json = GetResultText(tools.Search("a", 500));
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("matches").GetArrayLength().Should().BeLessThanOrEqualTo(25);
    }

    private static string GetResultText(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }
}
