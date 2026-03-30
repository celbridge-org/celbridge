using Celbridge.Server;
using Celbridge.Tools;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the QueryTools MCP tool methods.
/// </summary>
[TestFixture]
public class QueryToolTests
{
    private IApplicationServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Substitute.For<IApplicationServiceProvider>();
    }

    [Test]
    public void GetContext_ReturnsAgentContextMarkdown()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetContext());

        text.Should().Contain("# Celbridge Agent Context");
        text.Should().Contain("Resource Keys");
    }

    [Test]
    public void GetContext_ContainsContextPrioritizationGuidance()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetContext());

        text.Should().Contain("Context Prioritization");
        text.Should().Contain("document_get_context");
        text.Should().Contain("explorer_get_context");
    }

    private static string GetResultText(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }
}
