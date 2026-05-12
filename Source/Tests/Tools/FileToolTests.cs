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
    public async Task Replace_DispatchesCommand_AndReturnsCountAndAffectedLines()
    {
        var resource = new ResourceKey("notes/find.md");
        var path = Path.Combine(_tempFolder, "find.md");
        await File.WriteAllLinesAsync(path, new[] { "first new", "second new" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        IReplaceFileCommand? capturedCommand = null;
        var affectedRanges = new List<FileEditAffectedRange>
        {
            new(1, 1),
            new(2, 2)
        };
        _commandService
            .ExecuteAsync<IReplaceFileCommand, ReplaceFileResult>(
                Arg.Any<Action<IReplaceFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IReplaceFileCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IReplaceFileCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<ReplaceFileResult>.Ok(new ReplaceFileResult(2, affectedRanges, false)));
            });

        var tools = new FileTools(_services);
        var root = ParseResult(await tools.Replace("notes/find.md", "old", "new"));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.FileResource.Should().Be(resource);
        capturedCommand.SearchText.Should().Be("old");
        capturedCommand.ReplaceText.Should().Be("new");
        root.GetProperty("replacementCount").GetInt32().Should().Be(2);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();

        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(2);
        affected[0].GetProperty("from").GetInt32().Should().Be(1);
        affected[0].GetProperty("contextLines").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Replace_SurfacesTruncatedFlag_AndKeepsContextLinesOnSamples_WhenCommandReportsTruncation()
    {
        var resource = new ResourceKey("notes/many.md");
        var path = Path.Combine(_tempFolder, "many.md");
        await File.WriteAllLinesAsync(path, new[] { "Y", "Y", "Y", "Y", "Y", "Y", "Y", "Y" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var sampledRanges = new List<FileEditAffectedRange>
        {
            new(1, 1), new(2, 2), new(3, 3), new(8, 8)
        };
        _commandService
            .ExecuteAsync<IReplaceFileCommand, ReplaceFileResult>(
                Arg.Any<Action<IReplaceFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(Task.FromResult(Celbridge.Core.Result<ReplaceFileResult>.Ok(new ReplaceFileResult(8, sampledRanges, true))));

        var tools = new FileTools(_services);
        var root = ParseResult(await tools.Replace("notes/many.md", "x", "Y"));

        root.GetProperty("replacementCount").GetInt32().Should().Be(8);
        root.GetProperty("truncated").GetBoolean().Should().BeTrue();
        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(4);
        // contextLines stays attached to the sample entries — they are the
        // only verification signal a caller has for a truncated response.
        affected[0].TryGetProperty("contextLines", out var firstContext).Should().BeTrue();
        firstContext.GetArrayLength().Should().BeGreaterThan(0);
        affected[3].TryGetProperty("contextLines", out var lastContext).Should().BeTrue();
        lastContext.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Replace_SurfacesMatchCount_OnMergedAffectedLineEntries()
    {
        var resource = new ResourceKey("notes/merged.md");
        var path = Path.Combine(_tempFolder, "merged.md");
        await File.WriteAllLinesAsync(path, new[] { "THE THE THE" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var affectedRanges = new List<FileEditAffectedRange>
        {
            new(1, 1, 3)
        };
        _commandService
            .ExecuteAsync<IReplaceFileCommand, ReplaceFileResult>(
                Arg.Any<Action<IReplaceFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(Task.FromResult(Celbridge.Core.Result<ReplaceFileResult>.Ok(new ReplaceFileResult(3, affectedRanges, false))));

        var tools = new FileTools(_services);
        var root = ParseResult(await tools.Replace("notes/merged.md", "the", "THE"));

        root.GetProperty("replacementCount").GetInt32().Should().Be(3);
        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(1);
        affected[0].GetProperty("matchCount").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task Edit_ReturnsAffectedLineRanges_WhenSuccessful()
    {
        var resource = new ResourceKey("notes/edit.md");
        var path = Path.Combine(_tempFolder, "edit.md");
        await File.WriteAllLinesAsync(path, new[] { "alpha", "BETA", "gamma" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        IEditFileCommand? capturedCommand = null;
        var affectedRanges = new List<FileEditAffectedRange>
        {
            new(2, 2)
        };
        _commandService
            .ExecuteAsync<IEditFileCommand, EditFileResult>(
                Arg.Any<Action<IEditFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IEditFileCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IEditFileCommand>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<EditFileResult>.Ok(new EditFileResult(1, affectedRanges, false)));
            });

        var tools = new FileTools(_services);
        var root = ParseResult(await tools.Edit("notes/edit.md", "beta", "BETA"));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.FileResource.Should().Be(resource);
        capturedCommand.OldString.Should().Be("beta");
        capturedCommand.NewString.Should().Be("BETA");
        capturedCommand.ReplaceAll.Should().BeFalse();

        root.GetProperty("matchCount").GetInt32().Should().Be(1);
        root.GetProperty("truncated").GetBoolean().Should().BeFalse();
        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(1);
        affected[0].GetProperty("from").GetInt32().Should().Be(2);
        affected[0].GetProperty("to").GetInt32().Should().Be(2);

        var contextLines = affected[0].GetProperty("contextLines");
        var collected = new List<string>();
        for (var i = 0; i < contextLines.GetArrayLength(); i++)
        {
            collected.Add(contextLines[i].GetString()!);
        }
        collected.Should().Equal("alpha", "BETA", "gamma");
    }

    [Test]
    public async Task Edit_SurfacesTruncatedFlag_AndKeepsContextLinesOnSamples_WhenCommandReportsTruncation()
    {
        var resource = new ResourceKey("notes/many.md");
        var path = Path.Combine(_tempFolder, "many.md");
        await File.WriteAllLinesAsync(path, new[] { "Y", "Y", "Y", "Y", "Y", "Y", "Y", "Y" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var sampledRanges = new List<FileEditAffectedRange>
        {
            new(1, 1), new(2, 2), new(3, 3), new(8, 8)
        };
        _commandService
            .ExecuteAsync<IEditFileCommand, EditFileResult>(
                Arg.Any<Action<IEditFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(Task.FromResult(Celbridge.Core.Result<EditFileResult>.Ok(new EditFileResult(8, sampledRanges, true))));

        var tools = new FileTools(_services);
        var root = ParseResult(await tools.Edit("notes/many.md", "x", "Y", replaceAll: true));

        root.GetProperty("matchCount").GetInt32().Should().Be(8);
        root.GetProperty("truncated").GetBoolean().Should().BeTrue();
        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(4);
        // contextLines stays attached to the sample entries — they are the
        // only verification signal for a truncated response.
        affected[0].TryGetProperty("contextLines", out var firstContext).Should().BeTrue();
        firstContext.GetArrayLength().Should().BeGreaterThan(0);
        affected[3].TryGetProperty("contextLines", out var lastContext).Should().BeTrue();
        lastContext.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Edit_SurfacesMatchCount_OnMergedAffectedLineEntries()
    {
        // Command reports a merged range: three replaceAll hits on line 1
        // collapsed into one entry with MatchCount=3. The envelope must
        // surface that count on the affectedLines entry so the agent can
        // distinguish "one line, three hits" from "one line, one hit".
        var resource = new ResourceKey("notes/merged.md");
        var path = Path.Combine(_tempFolder, "merged.md");
        await File.WriteAllLinesAsync(path, new[] { "FOO bar FOO baz FOO" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var affectedRanges = new List<FileEditAffectedRange>
        {
            new(1, 1, 3)
        };
        _commandService
            .ExecuteAsync<IEditFileCommand, EditFileResult>(
                Arg.Any<Action<IEditFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(Task.FromResult(Celbridge.Core.Result<EditFileResult>.Ok(new EditFileResult(3, affectedRanges, false))));

        var tools = new FileTools(_services);
        var root = ParseResult(await tools.Edit("notes/merged.md", "foo", "FOO", replaceAll: true));

        root.GetProperty("matchCount").GetInt32().Should().Be(3);
        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(1);
        affected[0].GetProperty("from").GetInt32().Should().Be(1);
        affected[0].GetProperty("matchCount").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task Edit_ReturnsToolErrorWithMultiMatchHint_WhenMultipleOccurrences()
    {
        _commandService
            .ExecuteAsync<IEditFileCommand, EditFileResult>(
                Arg.Any<Action<IEditFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(Task.FromResult(Celbridge.Core.Result<EditFileResult>.Fail(
                "oldString matched 3 occurrences; add surrounding context to disambiguate, or set replaceAll: true")));

        var tools = new FileTools(_services);
        var result = await tools.Edit("notes/edit.md", "x", "y");

        result.IsError.Should().BeTrue();
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        text.Should().Contain("3 occurrences");
        text.Should().Contain("replaceAll");
    }

    [Test]
    public async Task MultiEdit_IncludesContextLines_ForNonTruncatedEdits()
    {
        var resource = new ResourceKey("notes/multi.md");
        var path = Path.Combine(_tempFolder, "multi.md");
        await File.WriteAllLinesAsync(path, new[] { "A", "two", "B", "C", "five" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        IMultiEditFileCommand? capturedCommand = null;
        var affectedRanges = new List<MultiEditFileAffectedRange>
        {
            new(0, 1, 1),
            new(1, 3, 4)
        };
        var editSummaries = new List<MultiEditFileEditSummary>
        {
            new(1, false),
            new(1, false)
        };
        _commandService
            .ExecuteAsync<IMultiEditFileCommand, MultiEditFileResult>(
                Arg.Any<Action<IMultiEditFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IMultiEditFileCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IMultiEditFileCommand>();
                    capturedCommand.Edits = new List<FileEditOperation>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<MultiEditFileResult>.Ok(new MultiEditFileResult(2, editSummaries, affectedRanges)));
            });

        var editsJson = "[{\"oldString\":\"a\",\"newString\":\"A\"},{\"oldString\":\"b\",\"newString\":\"B\\nC\"}]";

        var tools = new FileTools(_services);
        var root = ParseResult(await tools.MultiEdit("notes/multi.md", editsJson));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.FileResource.Should().Be(resource);
        capturedCommand.Edits.Should().HaveCount(2);

        root.GetProperty("appliedCount").GetInt32().Should().Be(2);

        var edits = root.GetProperty("edits");
        edits.GetArrayLength().Should().Be(2);
        edits[0].GetProperty("matchCount").GetInt32().Should().Be(1);
        edits[0].GetProperty("truncated").GetBoolean().Should().BeFalse();

        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(2);
        affected[0].GetProperty("editIndex").GetInt32().Should().Be(0);
        affected[1].GetProperty("editIndex").GetInt32().Should().Be(1);
        // contextLines is included for non-truncated edits.
        affected[0].TryGetProperty("contextLines", out var ctx0).Should().BeTrue();
        ctx0.GetArrayLength().Should().BeGreaterThan(0);
        affected[1].TryGetProperty("contextLines", out _).Should().BeTrue();
    }

    [Test]
    public async Task MultiEdit_SurfacesMatchCount_OnMergedAffectedLineEntries()
    {
        var resource = new ResourceKey("notes/merged.md");
        var path = Path.Combine(_tempFolder, "merged.md");
        await File.WriteAllLinesAsync(path, new[] { "FOO bar FOO" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var affectedRanges = new List<MultiEditFileAffectedRange>
        {
            new(0, 1, 1, 2)
        };
        var editSummaries = new List<MultiEditFileEditSummary>
        {
            new(2, false)
        };
        _commandService
            .ExecuteAsync<IMultiEditFileCommand, MultiEditFileResult>(
                Arg.Any<Action<IMultiEditFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IMultiEditFileCommand>?>();
                if (configure is not null)
                {
                    var captured = Substitute.For<IMultiEditFileCommand>();
                    captured.Edits = new List<FileEditOperation>();
                    configure(captured);
                }
                return Task.FromResult(Celbridge.Core.Result<MultiEditFileResult>.Ok(new MultiEditFileResult(1, editSummaries, affectedRanges)));
            });

        var editsJson = "[{\"oldString\":\"foo\",\"newString\":\"FOO\",\"replaceAll\":true}]";

        var tools = new FileTools(_services);
        var root = ParseResult(await tools.MultiEdit("notes/merged.md", editsJson));

        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(1);
        affected[0].GetProperty("editIndex").GetInt32().Should().Be(0);
        affected[0].GetProperty("matchCount").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task MultiEdit_KeepsContextLinesOnSamples_ForTruncatedEdits()
    {
        var resource = new ResourceKey("notes/multi.md");
        var path = Path.Combine(_tempFolder, "multi.md");
        await File.WriteAllLinesAsync(path, new[] { "Y", "Y", "Y", "Y", "Y", "Y", "Y", "Y" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var affectedRanges = new List<MultiEditFileAffectedRange>
        {
            new(0, 1, 1), new(0, 2, 2), new(0, 3, 3), new(0, 8, 8)
        };
        var editSummaries = new List<MultiEditFileEditSummary>
        {
            new(8, true)
        };
        _commandService
            .ExecuteAsync<IMultiEditFileCommand, MultiEditFileResult>(
                Arg.Any<Action<IMultiEditFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(Task.FromResult(Celbridge.Core.Result<MultiEditFileResult>.Ok(new MultiEditFileResult(1, editSummaries, affectedRanges))));

        var editsJson = "[{\"oldString\":\"x\",\"newString\":\"Y\",\"replaceAll\":true}]";

        var tools = new FileTools(_services);
        var root = ParseResult(await tools.MultiEdit("notes/multi.md", editsJson));

        root.GetProperty("appliedCount").GetInt32().Should().Be(1);
        var edits = root.GetProperty("edits");
        edits[0].GetProperty("truncated").GetBoolean().Should().BeTrue();

        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(4);
        // contextLines stays attached to sample entries — they are the only
        // verification signal for a truncated edit's contribution.
        affected[0].TryGetProperty("contextLines", out var firstContext).Should().BeTrue();
        firstContext.GetArrayLength().Should().BeGreaterThan(0);
        affected[3].TryGetProperty("contextLines", out var lastContext).Should().BeTrue();
        lastContext.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Test]
    public async Task MultiEdit_ReturnsToolErrorNamingFailingEditIndex_OnPartialFail()
    {
        _commandService
            .ExecuteAsync<IMultiEditFileCommand, MultiEditFileResult>(
                Arg.Any<Action<IMultiEditFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(Task.FromResult(Celbridge.Core.Result<MultiEditFileResult>.Fail(
                "Edit 1: oldString not found in file. Tried to match: 'nope'")));

        var editsJson = "[{\"oldString\":\"a\",\"newString\":\"A\"},{\"oldString\":\"nope\",\"newString\":\"x\"}]";

        var tools = new FileTools(_services);
        var result = await tools.MultiEdit("notes/multi.md", editsJson);

        result.IsError.Should().BeTrue();
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        text.Should().Contain("Edit 1");
        text.Should().Contain("not found");
    }

    [Test]
    public async Task MultiEdit_EmptyArray_SkipsCommandAndReturnsZeroAppliedCount()
    {
        var tools = new FileTools(_services);

        IMultiEditFileCommand? capturedCommand = null;
        _commandService
            .ExecuteAsync<IMultiEditFileCommand, MultiEditFileResult>(
                Arg.Any<Action<IMultiEditFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IMultiEditFileCommand>?>();
                if (configure is not null)
                {
                    capturedCommand = Substitute.For<IMultiEditFileCommand>();
                    capturedCommand.Edits = new List<FileEditOperation>();
                    configure(capturedCommand);
                }
                return Task.FromResult(Celbridge.Core.Result<MultiEditFileResult>.Ok(
                    new MultiEditFileResult(0, new List<MultiEditFileEditSummary>(), new List<MultiEditFileAffectedRange>())));
            });

        var root = ParseResult(await tools.MultiEdit("notes/multi.md", "[]"));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.Edits.Should().BeEmpty();
        root.GetProperty("appliedCount").GetInt32().Should().Be(0);
        root.GetProperty("edits").GetArrayLength().Should().Be(0);
        root.GetProperty("affectedLines").GetArrayLength().Should().Be(0);
    }

    [Test]
    public async Task Grep_FilesParameter_RejectsGlobString_WithActionableError()
    {
        // Agent passed a glob string where a JSON array of resource keys is
        // required. The error must name the expected shape and point at the
        // include parameter for glob-based scoping; surfacing the raw
        // JsonException ("'w' is an invalid start of a value") tells the
        // caller something is wrong but not what to type instead.
        var tools = new FileTools(_services);
        var result = await tools.Grep(searchTerm: "needle", files: "workspace/*.cs");

        result.IsError.Should().BeTrue();
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        text.Should().Contain("files takes a JSON array");
        text.Should().Contain("include");
    }

    [Test]
    public async Task Grep_FilesParameter_RejectsMalformedJsonArray_WithExampleShape()
    {
        var tools = new FileTools(_services);
        var result = await tools.Grep(searchTerm: "needle", files: "[\"folder/a.txt\",");

        result.IsError.Should().BeTrue();
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        text.Should().Contain("Invalid JSON array for files");
        text.Should().Contain("Expected a JSON array of resource keys");
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
