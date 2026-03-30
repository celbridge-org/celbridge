using System.Text.Json;
using Celbridge.Server;
using Celbridge.Tools;
using Celbridge.Workspace;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the DocumentTools MCP tool methods.
/// </summary>
[TestFixture]
public class DocumentToolTests
{
    private IApplicationServiceProvider _services = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IWorkspaceService _workspaceService = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Substitute.For<IApplicationServiceProvider>();
        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceService = Substitute.For<IWorkspaceService>();

        _workspaceWrapper.WorkspaceService.Returns(_workspaceService);
        _services.GetRequiredService<IWorkspaceWrapper>().Returns(_workspaceWrapper);
    }

    [Test]
    public void GetContext_ReturnsActiveDocument()
    {
        var documentsService = Substitute.For<IDocumentsService>();
        var documentsPanel = Substitute.For<IDocumentsPanel>();

        var activeResource = new ResourceKey("notes/readme.md");
        documentsService.ActiveDocument.Returns(activeResource);
        documentsService.OpenDocumentAddresses.Returns(new Dictionary<ResourceKey, DocumentAddress>
        {
            [activeResource] = new DocumentAddress(0, 0, 0)
        });
        documentsPanel.SectionCount.Returns(1);

        _workspaceService.DocumentsService.Returns(documentsService);
        _workspaceService.DocumentsPanel.Returns(documentsPanel);

        var tools = new DocumentTools(_services);
        var root = ParseResult(tools.GetContext());

        root.GetProperty("activeDocument").GetString().Should().Be("notes/readme.md");
        root.GetProperty("sectionCount").GetInt32().Should().Be(1);

        var openDocuments = root.GetProperty("openDocuments");
        openDocuments.GetArrayLength().Should().Be(1);

        var firstDocument = openDocuments[0];
        firstDocument.GetProperty("resource").GetString().Should().Be("notes/readme.md");
        firstDocument.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Test]
    public void GetContext_MultipleDocumentsAcrossSections()
    {
        var documentsService = Substitute.For<IDocumentsService>();
        var documentsPanel = Substitute.For<IDocumentsPanel>();

        var activeResource = new ResourceKey("src/main.py");
        var otherResource = new ResourceKey("tests/test_main.py");

        documentsService.ActiveDocument.Returns(activeResource);
        documentsService.OpenDocumentAddresses.Returns(new Dictionary<ResourceKey, DocumentAddress>
        {
            [activeResource] = new DocumentAddress(0, 0, 0),
            [otherResource] = new DocumentAddress(0, 1, 0)
        });
        documentsPanel.SectionCount.Returns(2);

        _workspaceService.DocumentsService.Returns(documentsService);
        _workspaceService.DocumentsPanel.Returns(documentsPanel);

        var tools = new DocumentTools(_services);
        var root = ParseResult(tools.GetContext());

        root.GetProperty("sectionCount").GetInt32().Should().Be(2);
        root.GetProperty("openDocuments").GetArrayLength().Should().Be(2);

        var documents = root.GetProperty("openDocuments");
        var activeDoc = documents.EnumerateArray().First(d => d.GetProperty("isActive").GetBoolean());
        activeDoc.GetProperty("resource").GetString().Should().Be("src/main.py");
        activeDoc.GetProperty("sectionIndex").GetInt32().Should().Be(0);

        var inactiveDoc = documents.EnumerateArray().First(d => !d.GetProperty("isActive").GetBoolean());
        inactiveDoc.GetProperty("resource").GetString().Should().Be("tests/test_main.py");
        inactiveDoc.GetProperty("sectionIndex").GetInt32().Should().Be(1);
    }

    [Test]
    public void GetContext_NoDocumentsOpen()
    {
        var documentsService = Substitute.For<IDocumentsService>();
        var documentsPanel = Substitute.For<IDocumentsPanel>();

        documentsService.ActiveDocument.Returns(ResourceKey.Empty);
        documentsService.OpenDocumentAddresses.Returns(new Dictionary<ResourceKey, DocumentAddress>());
        documentsPanel.SectionCount.Returns(1);

        _workspaceService.DocumentsService.Returns(documentsService);
        _workspaceService.DocumentsPanel.Returns(documentsPanel);

        var tools = new DocumentTools(_services);
        var root = ParseResult(tools.GetContext());

        root.GetProperty("activeDocument").GetString().Should().BeEmpty();
        root.GetProperty("openDocuments").GetArrayLength().Should().Be(0);
    }

    private static JsonElement ParseResult(CallToolResult result)
    {
        var json = result.Content.OfType<TextContentBlock>().Single().Text;
        return JsonDocument.Parse(json).RootElement;
    }
}
