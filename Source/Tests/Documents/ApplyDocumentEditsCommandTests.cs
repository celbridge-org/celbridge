using Celbridge.Documents.Commands;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers applying document edits: an open document changes in its editor, and a closed file changes on disk.
/// </summary>
[TestFixture]
public class ApplyDocumentEditsCommandTests
{
    private string _tempFolder = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IDocumentsPanel _documentsPanel = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ApplyDocumentEditsCommandTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(TestRootHandlers.Create(_tempFolder));
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        // A file is closed unless a test opens it.
        _documentsPanel = Substitute.For<IDocumentsPanel>();
        _documentsPanel.GetDocumentView(Arg.Any<ResourceKey>()).Returns((IDocumentView?)null);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        workspaceService.DocumentsPanel.Returns(_documentsPanel);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    [Test]
    public async Task ExecuteAsync_AClosedFile_ChangesOnDisk_AndTheResultListsTheFile()
    {
        var path = Path.Combine(_tempFolder, "shapes.ts");
        await File.WriteAllTextAsync(path, "export interface Circle {\n    radius: number;\n}\n");
        var command = CreateCommand(new FileRangeEdit(new ResourceKey("shapes.ts"), CreateRenameEdits(2)));

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("export interface Circle {\n    size: number;\n}\n");
        command.ResultValue.ChangedFiles.Should().Equal(new ResourceKey("shapes.ts"));
    }

    [Test]
    public async Task ExecuteAsync_AnOpenDocument_ChangesInItsEditor_AndTheFileOnDiskStays()
    {
        var path = Path.Combine(_tempFolder, "main.ts");
        await File.WriteAllTextAsync(path, "class Wheel {\n    radius = 2;\n}\n");
        var documentView = CreateOpenDocument(new ResourceKey("main.ts"), Result.Ok());
        var edits = CreateRenameEdits(2);
        var command = CreateCommand(new FileRangeEdit(new ResourceKey("main.ts"), edits));

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        await documentView.Received(1).ApplyEdits(edits);
        (await File.ReadAllTextAsync(path)).Should().Be("class Wheel {\n    radius = 2;\n}\n");
    }

    [Test]
    public async Task ExecuteAsync_AMissingFile_FailsBeforeAnyDocumentChanges()
    {
        var documentView = CreateOpenDocument(new ResourceKey("main.ts"), Result.Ok());
        var command = CreateCommand(
            new FileRangeEdit(new ResourceKey("main.ts"), CreateRenameEdits(2)),
            new FileRangeEdit(new ResourceKey("missing.ts"), CreateRenameEdits(2)));

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        await documentView.DidNotReceive().ApplyEdits(Arg.Any<IReadOnlyList<RangeEdit>>());
    }

    [Test]
    public async Task ExecuteAsync_ARangeOutsideAClosedFile_FailsBeforeAnyDocumentChanges()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "shapes.ts"), "export const unit = 1;\n");
        var documentView = CreateOpenDocument(new ResourceKey("main.ts"), Result.Ok());
        var command = CreateCommand(
            new FileRangeEdit(new ResourceKey("main.ts"), CreateRenameEdits(2)),
            new FileRangeEdit(new ResourceKey("shapes.ts"), CreateRenameEdits(9)));

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        await documentView.DidNotReceive().ApplyEdits(Arg.Any<IReadOnlyList<RangeEdit>>());
    }

    [Test]
    public async Task ExecuteAsync_AnEditorThatRefuses_LeavesTheClosedFilesAsTheyWere()
    {
        var path = Path.Combine(_tempFolder, "shapes.ts");
        await File.WriteAllTextAsync(path, "export interface Circle {\n    radius: number;\n}\n");
        CreateOpenDocument(new ResourceKey("main.ts"), Result.Fail("The editor refused the edits"));
        var command = CreateCommand(
            new FileRangeEdit(new ResourceKey("main.ts"), CreateRenameEdits(2)),
            new FileRangeEdit(new ResourceKey("shapes.ts"), CreateRenameEdits(2)));

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("export interface Circle {\n    radius: number;\n}\n");
    }

    private ApplyDocumentEditsCommand CreateCommand(params FileRangeEdit[] edits)
    {
        var command = new ApplyDocumentEditsCommand(_workspaceWrapper);
        command.Edits = edits.ToList();

        return command;
    }

    private IDocumentView CreateOpenDocument(ResourceKey resource, Result applyResult)
    {
        var documentView = Substitute.For<IDocumentView>();
        documentView.ApplyEdits(Arg.Any<IReadOnlyList<RangeEdit>>()).Returns(applyResult);
        _documentsPanel.GetDocumentView(resource).Returns(documentView);

        return documentView;
    }

    // Renames "radius" to "size" at the start of the given line's indented text.
    private static List<RangeEdit> CreateRenameEdits(int line)
    {
        return new List<RangeEdit>
        {
            new(line, 5, line, 11, "size"),
        };
    }
}
