using Celbridge.Documents.Commands;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers GetDocumentStateCommand's snapshot building: the active document, the visible
/// sections, and the list of open documents.
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
            new(activeDocument, new DocumentAddress(0, DocumentSectionId.MainLeft, 0), EditorId.Empty),
            new(otherDocument, new DocumentAddress(0, DocumentSectionId.MainRight, 0), EditorId.Empty),
        };

        var visibleSections = new List<DocumentSectionId>
        {
            DocumentSectionId.MainLeft,
            DocumentSectionId.MainRight
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
        documentsService.VisibleSections.Returns(new List<DocumentSectionId> { DocumentSectionId.MainLeft });
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
}
