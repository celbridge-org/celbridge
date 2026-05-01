using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Resources;
using Celbridge.Server;
using Celbridge.Tools;
using Celbridge.Workspace;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for the FileTools MCP tool methods that perform on-disk file edits.
/// </summary>
[TestFixture]
public class FileToolTests
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

        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(FileToolTests), Guid.NewGuid().ToString("N"));
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
                    capturedCommand.Edits = new List<FileEdit>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<IReadOnlyList<AppliedEdit>>.Ok(appliedRanges));
            });

        var editsJson = "[{\"line\": 2, \"endLine\": 2, \"newText\": \"Replaced\"}]";

        var tools = new FileTools(_services);
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

        var tools = new FileTools(_services);
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
        IWriteFileCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<IWriteFileCommand>(
                Arg.Any<Action<IWriteFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IWriteFileCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IWriteFileCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new FileTools(_services);
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
        IFindReplaceFileCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<IFindReplaceFileCommand, int>(
                Arg.Any<Action<IFindReplaceFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IFindReplaceFileCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IFindReplaceFileCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<int>.Ok(7));
            });

        var tools = new FileTools(_services);
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

        var tools = new FileTools(_services);
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
        IWriteBinaryFileCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<IWriteBinaryFileCommand>(
                Arg.Any<Action<IWriteBinaryFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IWriteBinaryFileCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IWriteBinaryFileCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new FileTools(_services);
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
