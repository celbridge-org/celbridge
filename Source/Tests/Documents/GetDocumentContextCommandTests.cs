using Celbridge.Documents.Commands;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Direct unit test for GetDocumentContextCommand. Complements the DocumentTools tests which
/// stub the command entirely and therefore don't exercise the command's own snapshot-building
/// logic.
/// </summary>
[TestFixture]
public class GetDocumentContextCommandTests
{
    [Test]
    public async Task Execute_CapturesActiveDocumentSectionCountAndOpenList()
    {
        var activeDocument = new ResourceKey("notes/readme.md");
        var otherDocument = new ResourceKey("src/main.cs");
        var openDocuments = new List<OpenDocumentInfo>
        {
            new(activeDocument, new DocumentAddress(0, 0, 0), DocumentEditorId.Empty),
            new(otherDocument, new DocumentAddress(0, 1, 0), DocumentEditorId.Empty),
        };

        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.ActiveDocument.Returns(activeDocument);
        documentsService.SectionCount.Returns(2);
        documentsService.GetOpenDocuments().Returns(openDocuments);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(documentsService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var command = new GetDocumentContextCommand(workspaceWrapper);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var snapshot = command.ResultValue;
        snapshot.ActiveDocument.Should().Be(activeDocument);
        snapshot.SectionCount.Should().Be(2);
        snapshot.OpenDocuments.Should().BeEquivalentTo(openDocuments);
    }

    [Test]
    public async Task Execute_WithNoOpenDocuments_ReturnsEmptyList()
    {
        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.ActiveDocument.Returns(ResourceKey.Empty);
        documentsService.SectionCount.Returns(1);
        documentsService.GetOpenDocuments().Returns(Array.Empty<OpenDocumentInfo>());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(documentsService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var command = new GetDocumentContextCommand(workspaceWrapper);

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ActiveDocument.Should().Be(ResourceKey.Empty);
        command.ResultValue.OpenDocuments.Should().BeEmpty();
    }
}
