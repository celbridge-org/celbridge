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
    public void GetContext_ContainsJavaScriptExtensionSection()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetContext());

        text.Should().Contain("Writing Package Extensions (JavaScript)");
        text.Should().Contain("query_get_javascript_api");
        text.Should().Contain("requires_tools");
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

    [Test]
    public void GetJavaScriptApi_ReturnsApiReference()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetJavaScriptApi());

        text.Should().Contain("# Celbridge JavaScript API Reference");
    }

    [Test]
    public void GetJavaScriptApi_ContainsAllNamespaces()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetJavaScriptApi());

        text.Should().Contain("## app");
        text.Should().Contain("## document");
        text.Should().Contain("## explorer");
        text.Should().Contain("## file");
        text.Should().Contain("## package");
        text.Should().Contain("## query");
    }

    [Test]
    public void GetJavaScriptApi_UsesCamelCaseParameters()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetJavaScriptApi());

        // Parameters should be camelCase (e.g. fileResource), not snake_case (file_resource)
        text.Should().Contain("fileResource: string");
        text.Should().NotContain("file_resource: string");
        text.Should().Contain("editsJson: string");
    }

    [Test]
    public void GetJavaScriptApi_UsesCamelCaseMethodNames()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetJavaScriptApi());

        // Method names from snake_case aliases become camelCase (e.g. apply_edits -> applyEdits)
        text.Should().Contain("cel.document.applyEdits(");
        text.Should().Contain("cel.explorer.getContext()");
        text.Should().NotContain("cel.document.apply_edits(");
    }

    [Test]
    public void GetJavaScriptApi_ContainsPromiseReturnTypes()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetJavaScriptApi());

        text.Should().Contain(": Promise<");
    }

    [Test]
    public void GetJavaScriptApi_DocumentsRequiresTools()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetJavaScriptApi());

        text.Should().Contain("requires_tools");
        text.Should().Contain("CEL_TOOL_DENIED");
    }

    [Test]
    public void GetJavaScriptApi_DocumentsCelToolError()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetJavaScriptApi());

        text.Should().Contain("CelToolError");
    }

    [Test]
    public void GetJavaScriptApi_CompactFormat_NoMarkdownHeadingsPerMethod()
    {
        var tools = new QueryTools(_services);
        var text = GetResultText(tools.GetJavaScriptApi());

        text.Should().NotContain("### cel.app.");
        text.Should().Contain("  cel.app.getStatus(");
    }

    private static string GetResultText(CallToolResult result)
    {
        return result.Content.OfType<TextContentBlock>().Single().Text;
    }
}
