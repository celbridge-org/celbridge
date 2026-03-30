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

    [Test]
    public void GetPythonApi_ReturnsApiReference()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetPythonApi());

        text.Should().Contain("# Celbridge Python API Reference");
    }

    [Test]
    public void GetPythonApi_ContainsAllNamespaces()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetPythonApi());

        text.Should().Contain("## app");
        text.Should().Contain("## document");
        text.Should().Contain("## explorer");
        text.Should().Contain("## file");
        text.Should().Contain("## package");
        text.Should().Contain("## query");
    }

    [Test]
    public void GetPythonApi_ContainsMethodSignatures()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetPythonApi());

        text.Should().Contain("document.apply_edits(");
        text.Should().Contain("file_resource: str");
        text.Should().Contain("file.read(");
        text.Should().Contain("explorer.get_context()");
    }

    [Test]
    public void GetPythonApi_ContainsReturnTypeAnnotations()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetPythonApi());

        // Tools with /// <returns> tags should have return type annotations
        text.Should().Contain("-> ");
    }

    [Test]
    public void GetPythonApi_CompactFormat_NoMarkdownHeadingsPerMethod()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetPythonApi());

        // Methods should be listed compactly with indent, not as ### headings
        text.Should().NotContain("### app.");
        text.Should().Contain("  app.get_status(");
    }

    private static string GetResultText(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }
}
