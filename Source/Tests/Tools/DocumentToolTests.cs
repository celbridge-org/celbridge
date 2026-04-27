using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Resources;
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
    private ICommandService _commandService = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private string _tempFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Substitute.For<IApplicationServiceProvider>();
        _commandService = Substitute.For<ICommandService>();

        _services.GetRequiredService<ICommandService>().Returns(_commandService);

        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(DocumentToolTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _resourceRegistry = Substitute.For<IResourceRegistry>();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _services.GetRequiredService<IWorkspaceWrapper>().Returns(workspaceWrapper);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
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

    [Test]
    public async Task ApplyEdits_DispatchesCommandAndReturnsAffectedLineRanges()
    {
        var resource = new ResourceKey("notes/edit.md");
        var path = Path.Combine(_tempFolder, "edit.md");
        await File.WriteAllLinesAsync(path, new[] { "First", "Replaced", "Third" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        IApplyEditsCommand? capturedCommand = null;
        IReadOnlyList<AppliedEdit> appliedRanges = new[]
        {
            new AppliedEdit(resource, 2, 2)
        };
        _commandService
            .ExecuteAsync<IApplyEditsCommand, IReadOnlyList<AppliedEdit>>(
                Arg.Any<Action<IApplyEditsCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IApplyEditsCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IApplyEditsCommand>();
                    capturedCommand.Edits = new List<DocumentEdit>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<IReadOnlyList<AppliedEdit>>.Ok(appliedRanges));
            });

        var editsJson = "[{\"line\": 2, \"endLine\": 2, \"newText\": \"Replaced\"}]";

        var tools = new DocumentTools(_services);
        var root = ParseResult(await tools.ApplyEdits("notes/edit.md", editsJson));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Edits.Should().HaveCount(1);
        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(1);
        affected[0].GetProperty("from").GetInt32().Should().Be(2);
        affected[0].GetProperty("to").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task ApplyEdits_ContextWindowCoversFullPostEditRange_ForLineExpandingEdit()
    {
        var resource = new ResourceKey("notes/expand.md");
        var path = Path.Combine(_tempFolder, "expand.md");
        // Post-edit content: line 2 was replaced with three lines (Two, Inserted, Three).
        await File.WriteAllLinesAsync(path, new[] { "First", "Two", "Inserted", "Three", "Last" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        IReadOnlyList<AppliedEdit> appliedRanges = new[]
        {
            new AppliedEdit(resource, 2, 4)
        };
        _commandService
            .ExecuteAsync<IApplyEditsCommand, IReadOnlyList<AppliedEdit>>(
                Arg.Any<Action<IApplyEditsCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(Task.FromResult(Celbridge.Core.Result<IReadOnlyList<AppliedEdit>>.Ok(appliedRanges)));

        var editsJson = "[{\"line\": 2, \"endLine\": 2, \"newText\": \"Two\\nInserted\\nThree\"}]";

        var tools = new DocumentTools(_services);
        var root = ParseResult(await tools.ApplyEdits("notes/expand.md", editsJson));

        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(1);
        affected[0].GetProperty("from").GetInt32().Should().Be(2);
        affected[0].GetProperty("to").GetInt32().Should().Be(4);

        // Context window = 1 line before + the 3 post-edit lines + 1 line after.
        var contextLines = affected[0].GetProperty("contextLines");
        var lines = new List<string>();
        for (var i = 0; i < contextLines.GetArrayLength(); i++)
        {
            lines.Add(contextLines[i].GetString()!);
        }
        lines.Should().Equal("First", "Two", "Inserted", "Three", "Last");
    }

    [Test]
    public async Task Write_DispatchesCommand_AndReturnsLineCount()
    {
        var resource = new ResourceKey("notes/new.md");
        IWriteDocumentCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<IWriteDocumentCommand>(
                Arg.Any<Action<IWriteDocumentCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IWriteDocumentCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IWriteDocumentCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new DocumentTools(_services);
        var root = ParseResult(await tools.Write("notes/new.md", "line one\nline two\n"));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.FileResource.Should().Be(resource);
        capturedCommand.Content.Should().Be("line one\nline two\n");
        // Canonical (ReadAllLines) semantics: a trailing newline does not add
        // a phantom empty line, so "line one\nline two\n" is 2 lines.
        root.GetProperty("lineCount").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task FindReplace_DispatchesCommand_AndReturnsReplacementCount()
    {
        var resource = new ResourceKey("notes/find.md");
        IFindReplaceDocumentCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<IFindReplaceDocumentCommand, int>(
                Arg.Any<Action<IFindReplaceDocumentCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IFindReplaceDocumentCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IFindReplaceDocumentCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<int>.Ok(7));
            });

        var tools = new DocumentTools(_services);
        var root = ParseResult(await tools.FindReplace("notes/find.md", "old", "new"));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.FileResource.Should().Be(resource);
        capturedCommand.SearchText.Should().Be("old");
        capturedCommand.ReplaceText.Should().Be("new");
        root.GetProperty("replacementCount").GetInt32().Should().Be(7);
    }

    [Test]
    public async Task DeleteLines_DispatchesCommand_AndReturnsDeletedRange()
    {
        var resource = new ResourceKey("notes/lines.md");
        var path = Path.Combine(_tempFolder, "lines.md");
        await File.WriteAllLinesAsync(path, new[] { "Line one", "Line four" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        IDeleteLinesCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<IDeleteLinesCommand>(
                Arg.Any<Action<IDeleteLinesCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IDeleteLinesCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IDeleteLinesCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new DocumentTools(_services);
        var root = ParseResult(await tools.DeleteLines("notes/lines.md", 2, 3));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Resource.Should().Be(resource);
        capturedCommand.StartLine.Should().Be(2);
        capturedCommand.EndLine.Should().Be(3);
        root.GetProperty("deletedFrom").GetInt32().Should().Be(2);
        root.GetProperty("deletedTo").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task WriteBinary_DispatchesCommand_AndReturnsOk()
    {
        var resource = new ResourceKey("notes/data.bin");
        IWriteBinaryDocumentCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<IWriteBinaryDocumentCommand>(
                Arg.Any<Action<IWriteBinaryDocumentCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IWriteBinaryDocumentCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IWriteBinaryDocumentCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new DocumentTools(_services);
        var result = await tools.WriteBinary("notes/data.bin", Convert.ToBase64String(new byte[] { 1, 2, 3 }));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.FileResource.Should().Be(resource);
        capturedCommand.Base64Content.Should().Be(Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        result.IsError.Should().NotBe(true);
    }

    private static JsonElement ParseResult(CallToolResult result)
    {
        var json = result.Content.OfType<TextContentBlock>().Single().Text;
        return JsonDocument.Parse(json).RootElement;
    }
}
