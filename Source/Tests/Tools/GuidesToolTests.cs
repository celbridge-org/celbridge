using System.Text.Json;
using Celbridge.Server;
using Celbridge.Tools;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the GuidesTools MCP tool methods. Also exercises the embedded
/// guide library loader: missing frontmatter, name collisions, per-tool
/// guides whose name doesn't match a registered tool, and registered tools
/// that lack a per-tool guide all surface here rather than on first agent call.
/// </summary>
[TestFixture]
public class GuidesToolTests
{
    // Hand-rolled stub instead of substituting IApplicationServiceProvider
    // wholesale, so GuidesTools can resolve the internal IGuides without
    // forcing Castle DynamicProxy to generate a proxy for it (which would
    // require an InternalsVisibleTo("DynamicProxyGenAssembly2") entry on
    // Celbridge.Tools). Other services (e.g. ICommandService, resolved
    // eagerly by AgentToolBase) are public interfaces and fall through to
    // NSubstitute — the guides tools never call them, so a no-op auto-mock
    // is fine.
    private sealed class TestServiceProvider : IApplicationServiceProvider
    {
        public IGuides Guides { get; }

        public TestServiceProvider()
        {
            var library = new Guides();
            library.Load();
            Guides = library;
        }

        public T GetRequiredService<T>() where T : class
        {
            if (Guides is T guides)
            {
                return guides;
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
    public void List_ReturnsConceptGuidesBeforeToolGuides()
    {
        var tools = new GuidesTools(_services);
        var json = GetResultText(tools.List());
        var root = JsonDocument.Parse(json).RootElement;
        var guides = root.GetProperty("guides");

        guides.GetArrayLength().Should().BeGreaterThan(0);

        var lastConceptIndex = -1;
        var firstToolIndex = int.MaxValue;
        for (int index = 0; index < guides.GetArrayLength(); index++)
        {
            var kind = guides[index].GetProperty("kind").GetString();
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
        var tools = new GuidesTools(_services);
        var json = GetResultText(tools.List());
        var root = JsonDocument.Parse(json).RootElement;
        var firstEntry = root.GetProperty("guides")[0];

        firstEntry.GetProperty("name").GetString().Should().Be("getting_started");
        firstEntry.GetProperty("kind").GetString().Should().Be("concept");
    }

    [Test]
    public void Read_ResolvesConceptGuide()
    {
        var tools = new GuidesTools(_services);
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
        var tools = new GuidesTools(_services);
        var json = GetResultText(tools.Read("[\"definitely_not_a_real_guide\"]"));
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("results").GetArrayLength().Should().Be(0);
        var unknown = root.GetProperty("unknown");
        unknown.GetArrayLength().Should().Be(1);
        unknown[0].GetString().Should().Be("definitely_not_a_real_guide");
    }

    [Test]
    public void Read_PerToolGuideCarriesPythonAndJavaScriptInvocations()
    {
        var tools = new GuidesTools(_services);
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
    public void Search_FindsResourceKeysGuide()
    {
        var tools = new GuidesTools(_services);
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
        var tools = new GuidesTools(_services);
        var json = GetResultText(tools.Search("[unclosed", 10));
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("matches").GetArrayLength().Should().Be(0);
        root.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public void Search_ClampsLimitAboveMax()
    {
        var tools = new GuidesTools(_services);
        var json = GetResultText(tools.Search("a", 500));
        var root = JsonDocument.Parse(json).RootElement;

        root.GetProperty("matches").GetArrayLength().Should().BeLessThanOrEqualTo(25);
    }

    private static string GetResultText(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }
}
