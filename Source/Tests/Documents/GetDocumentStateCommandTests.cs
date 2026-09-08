using Celbridge.Documents.Commands;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers GetDocumentStateCommand's snapshot building: the active document, the visible
/// sections, the list of open documents, and the health each document reports.
/// </summary>
[TestFixture]
public class GetDocumentStateCommandTests
{
    [Test]
    public async Task Execute_CapturesActiveDocumentVisibleSectionsAndOpenList()
    {
        var activeDocument = new ResourceKey("notes/readme.md");
        var otherDocument = new ResourceKey("src/main.cs");
        var openDocuments = new List<OpenDocumentInfo>
        {
            new(activeDocument, new DocumentAddress(0, DocumentSection.MainLeft, 0), EditorId.Empty),
            new(otherDocument, new DocumentAddress(0, DocumentSection.MainRight, 0), EditorId.Empty),
        };

        var visibleSections = new List<DocumentSection>
        {
            DocumentSection.MainLeft,
            DocumentSection.MainRight
        };

        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.ActiveDocument.Returns(activeDocument);
        documentsService.VisibleSections.Returns(visibleSections);
        documentsService.GetOpenDocuments().Returns(openDocuments);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(documentsService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var command = new GetDocumentStateCommand(workspaceWrapper);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var snapshot = command.ResultValue;
        snapshot.ActiveDocument.Should().Be(activeDocument);
        snapshot.VisibleSections.Should().Equal(visibleSections);
        snapshot.OpenDocuments.Should().BeEquivalentTo(openDocuments);
    }

    [Test]
    public async Task Execute_WithNoOpenDocuments_ReturnsEmptyList()
    {
        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.ActiveDocument.Returns(ResourceKey.Empty);
        documentsService.VisibleSections.Returns(new List<DocumentSection> { DocumentSection.MainLeft });
        documentsService.GetOpenDocuments().Returns(Array.Empty<OpenDocumentInfo>());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(documentsService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var command = new GetDocumentStateCommand(workspaceWrapper);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ActiveDocument.Should().Be(ResourceKey.Empty);
        command.ResultValue.OpenDocuments.Should().BeEmpty();
    }

    [Test]
    public async Task Execute_CollectsHealthFromTheDocumentViews()
    {
        var sickDocument = new ResourceKey("notes/readme.md");
        var wellDocument = new ResourceKey("src/main.cs");

        var sickView = Substitute.For<IDocumentView>();
        sickView.GetHealth().Returns(new DocumentHealth(WakeFailures: 4, ProcessFailures: 2));

        var wellView = Substitute.For<IDocumentView>();
        wellView.GetHealth().Returns(DocumentHealth.Healthy);

        var documentsPanel = Substitute.For<IDocumentsPanel>();
        documentsPanel.GetDocumentView(sickDocument).Returns(sickView);
        documentsPanel.GetDocumentView(wellDocument).Returns(wellView);

        var command = BuildCommand(
            new List<OpenDocumentInfo>
            {
                new(sickDocument, new DocumentAddress(0, DocumentSection.MainLeft, 0), EditorId.Empty),
                new(wellDocument, new DocumentAddress(0, DocumentSection.MainLeft, 1), EditorId.Empty),
            },
            documentsPanel);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();

        // Only the document with something to report is carried.
        var unhealthyDocuments = command.ResultValue.UnhealthyDocuments;
        unhealthyDocuments.Should().ContainKey(sickDocument);
        unhealthyDocuments[sickDocument].WakeFailures.Should().Be(4);
        unhealthyDocuments[sickDocument].ProcessFailures.Should().Be(2);
        unhealthyDocuments.Should().NotContainKey(wellDocument);
    }

    [Test]
    public async Task Execute_WithAllDocumentsHealthy_ReportsNone()
    {
        var document = new ResourceKey("notes/readme.md");

        var documentView = Substitute.For<IDocumentView>();
        documentView.GetHealth().Returns(DocumentHealth.Healthy);

        var documentsPanel = Substitute.For<IDocumentsPanel>();
        documentsPanel.GetDocumentView(document).Returns(documentView);

        var command = BuildCommand(
            new List<OpenDocumentInfo>
            {
                new(document, new DocumentAddress(0, DocumentSection.MainLeft, 0), EditorId.Empty),
            },
            documentsPanel);

        await command.ExecuteAsync();

        command.ResultValue.UnhealthyDocuments.Should().BeEmpty();
    }

    [Test]
    public async Task Execute_WithNoViewForAnOpenDocument_ReportsItHealthy()
    {
        var document = new ResourceKey("notes/readme.md");

        // A document open in the model but with no view yet has nothing that can be unwell.
        var documentsPanel = Substitute.For<IDocumentsPanel>();
        documentsPanel.GetDocumentView(document).Returns((IDocumentView?)null);

        var command = BuildCommand(
            new List<OpenDocumentInfo>
            {
                new(document, new DocumentAddress(0, DocumentSection.MainLeft, 0), EditorId.Empty),
            },
            documentsPanel);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.UnhealthyDocuments.Should().BeEmpty();
    }

    private static GetDocumentStateCommand BuildCommand(
        IReadOnlyList<OpenDocumentInfo> openDocuments,
        IDocumentsPanel documentsPanel)
    {
        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.ActiveDocument.Returns(ResourceKey.Empty);
        documentsService.VisibleSections.Returns(new List<DocumentSection> { DocumentSection.MainLeft });
        documentsService.GetOpenDocuments().Returns(openDocuments);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(documentsService);
        workspaceService.DocumentsPanel.Returns(documentsPanel);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        return new GetDocumentStateCommand(workspaceWrapper);
    }
}
