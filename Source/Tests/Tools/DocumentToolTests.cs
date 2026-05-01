using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Server;
using Celbridge.Tools;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the DocumentTools MCP tool methods that manage the editor surface.
/// </summary>
[TestFixture]
public class DocumentToolTests
{
    private IApplicationServiceProvider _services = null!;
    private ICommandService _commandService = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Substitute.For<IApplicationServiceProvider>();
        _commandService = Substitute.For<ICommandService>();

        _services.GetRequiredService<ICommandService>().Returns(_commandService);
    }

    /// <summary>
    /// Configures the mocked command service to return the given snapshot when the
    /// GetContext query command is executed. Returns Result.Ok(snapshot) so the tool
    /// treats it as a successful query.
    /// </summary>
    private void StubGetContextSnapshot(DocumentContextSnapshot snapshot)
    {
        _commandService
            .ExecuteAsync<IGetDocumentContextCommand, DocumentContextSnapshot>(
                Arg.Any<Action<IGetDocumentContextCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(Result<DocumentContextSnapshot>.Ok(snapshot));
    }

    [Test]
    public async Task GetContext_ReturnsActiveDocument()
    {
        var activeResource = new ResourceKey("notes/readme.md");
        var snapshot = new DocumentContextSnapshot(
            activeResource,
            1,
            new List<OpenDocumentInfo>
            {
                new(activeResource, new DocumentAddress(0, 0, 0), DocumentEditorId.Empty)
            });
        StubGetContextSnapshot(snapshot);

        var tools = new DocumentTools(_services);
        var root = ParseResult(await tools.GetContext());

        root.GetProperty("activeDocument").GetString().Should().Be("notes/readme.md");
        root.GetProperty("sectionCount").GetInt32().Should().Be(1);

        var openDocuments = root.GetProperty("openDocuments");
        openDocuments.GetArrayLength().Should().Be(1);

        var firstDocument = openDocuments[0];
        firstDocument.GetProperty("resource").GetString().Should().Be("notes/readme.md");
        firstDocument.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task GetContext_MultipleDocumentsAcrossSections()
    {
        var activeResource = new ResourceKey("src/main.py");
        var otherResource = new ResourceKey("tests/test_main.py");
        var snapshot = new DocumentContextSnapshot(
            activeResource,
            2,
            new List<OpenDocumentInfo>
            {
                new(activeResource, new DocumentAddress(0, 0, 0), DocumentEditorId.Empty),
                new(otherResource, new DocumentAddress(0, 1, 0), DocumentEditorId.Empty)
            });
        StubGetContextSnapshot(snapshot);

        var tools = new DocumentTools(_services);
        var root = ParseResult(await tools.GetContext());

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
    public async Task GetContext_IncludesEditorIdForEachOpenDocument()
    {
        var resource = new ResourceKey("packages/widget/index.html");
        var snapshot = new DocumentContextSnapshot(
            resource,
            1,
            new List<OpenDocumentInfo>
            {
                new(resource, new DocumentAddress(0, 0, 0), new DocumentEditorId("celbridge.html-viewer"))
            });
        StubGetContextSnapshot(snapshot);

        var tools = new DocumentTools(_services);
        var root = ParseResult(await tools.GetContext());

        var firstDocument = root.GetProperty("openDocuments")[0];
        firstDocument.GetProperty("editorId").GetString().Should().Be("celbridge.html-viewer");
    }

    [Test]
    public async Task GetContext_EmitsEmptyEditorIdWhenUnbound()
    {
        var resource = new ResourceKey("notes/readme.md");
        var snapshot = new DocumentContextSnapshot(
            resource,
            1,
            new List<OpenDocumentInfo>
            {
                new(resource, new DocumentAddress(0, 0, 0), DocumentEditorId.Empty)
            });
        StubGetContextSnapshot(snapshot);

        var tools = new DocumentTools(_services);
        var root = ParseResult(await tools.GetContext());

        var firstDocument = root.GetProperty("openDocuments")[0];
        firstDocument.GetProperty("editorId").GetString().Should().BeEmpty();
    }

    [Test]
    public async Task GetContext_NoDocumentsOpen()
    {
        var snapshot = new DocumentContextSnapshot(
            ResourceKey.Empty,
            1,
            new List<OpenDocumentInfo>());
        StubGetContextSnapshot(snapshot);

        var tools = new DocumentTools(_services);
        var root = ParseResult(await tools.GetContext());

        root.GetProperty("activeDocument").GetString().Should().BeEmpty();
        root.GetProperty("openDocuments").GetArrayLength().Should().Be(0);
    }

    private static JsonElement ParseResult(CallToolResult result)
    {
        var json = result.Content.OfType<TextContentBlock>().Single().Text;
        return JsonDocument.Parse(json).RootElement;
    }
}
