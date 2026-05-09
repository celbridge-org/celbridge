using System.Text.Json;
using Celbridge.Server;
using Celbridge.Tools;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the GuidesTools MCP tool methods. Also exercises the embedded
/// guide library loader: per-tool guides whose name doesn't match a registered
/// tool, and registered tools that lack a per-tool guide all surface here
/// rather than on first agent call.
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
        // Per-tool guides resolve to a tool entry that carries the language
        // invocation strings alongside the body. The Guides loader enforces
        // a per-tool guide for every registered MCP tool, so this never
        // falls back to an unknown response.
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

    private static string GetResultText(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }
}
