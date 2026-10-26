using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;
using Celbridge.Search;
using Celbridge.Server;
using Celbridge.Tests.FileSystem;
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
    private RootHandlerRegistry _rootHandlers = null!;
    private IResourceService _resourceService = null!;
    private string _tempFolder = null!;

    [SetUp]
    public void SetUp()
    {
        _services = Substitute.For<IApplicationServiceProvider>();
        _commandService = Substitute.For<ICommandService>();

        _services.GetRequiredService<ICommandService>().Returns(_commandService);

        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(FileToolTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _rootHandlers = TestRootHandlers.Create(_tempFolder);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(_rootHandlers);
        _resourceService = resourceService;

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        // Wire a real LocalResourceFileSystem against the temp folder so the
        // gateway reads tests rely on probe and read the actual files.
        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);

        // SidecarService is a pure-string predicate over the resource key; the
        // real implementation is fine in tests.
        resourceService.Sidecars.Returns(new SidecarService(workspaceWrapper));

        var walker = new ResourceWalker(Substitute.For<ILogger<ResourceWalker>>(), workspaceWrapper);
        resourceService.Walker.Returns(walker);

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
        var resource = new ResourceKey("find.md");
        var path = Path.Combine(_tempFolder, "find.md");
        await File.WriteAllLinesAsync(path, new[] { "first new", "second new" });

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
        var root = ParseResult(await tools.Replace("find.md", "old", "new"));

        capturedCommand.Should().NotBeNull();
        capturedCommand!.FileResource.Should().Be(resource);
        capturedCommand.Pattern.Should().Be("old");
        capturedCommand.Replacement.Should().Be("new");
        capturedCommand.Literal.Should().BeTrue();
        capturedCommand.MatchCase.Should().BeTrue();
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
        var resource = new ResourceKey("many.md");
        var path = Path.Combine(_tempFolder, "many.md");
        await File.WriteAllLinesAsync(path, new[] { "Y", "Y", "Y", "Y", "Y", "Y", "Y", "Y" });

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
        var root = ParseResult(await tools.Replace("many.md", "x", "Y"));

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
        var resource = new ResourceKey("merged.md");
        var path = Path.Combine(_tempFolder, "merged.md");
        await File.WriteAllLinesAsync(path, new[] { "THE THE THE" });

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
        var root = ParseResult(await tools.Replace("merged.md", "the", "THE"));

        root.GetProperty("replacementCount").GetInt32().Should().Be(3);
        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(1);
        affected[0].GetProperty("matchCount").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task Edit_ReturnsAffectedLineRanges_WhenSuccessful()
    {
        var resource = new ResourceKey("edit.md");
        var path = Path.Combine(_tempFolder, "edit.md");
        await File.WriteAllLinesAsync(path, new[] { "alpha", "BETA", "gamma" });

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
        var root = ParseResult(await tools.Edit("edit.md", "beta", "BETA"));

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
        var resource = new ResourceKey("many.md");
        var path = Path.Combine(_tempFolder, "many.md");
        await File.WriteAllLinesAsync(path, new[] { "Y", "Y", "Y", "Y", "Y", "Y", "Y", "Y" });

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
        var root = ParseResult(await tools.Edit("many.md", "x", "Y", replaceAll: true));

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
        var resource = new ResourceKey("merged.md");
        var path = Path.Combine(_tempFolder, "merged.md");
        await File.WriteAllLinesAsync(path, new[] { "FOO bar FOO baz FOO" });

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
        var root = ParseResult(await tools.Edit("merged.md", "foo", "FOO", replaceAll: true));

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
        var resource = new ResourceKey("multi.md");
        var path = Path.Combine(_tempFolder, "multi.md");
        await File.WriteAllLinesAsync(path, new[] { "A", "two", "B", "C", "five" });

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
        var root = ParseResult(await tools.MultiEdit("multi.md", editsJson));

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
        var resource = new ResourceKey("merged.md");
        var path = Path.Combine(_tempFolder, "merged.md");
        await File.WriteAllLinesAsync(path, new[] { "FOO bar FOO" });

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
        var root = ParseResult(await tools.MultiEdit("merged.md", editsJson));

        var affected = root.GetProperty("affectedLines");
        affected.GetArrayLength().Should().Be(1);
        affected[0].GetProperty("editIndex").GetInt32().Should().Be(0);
        affected[0].GetProperty("matchCount").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task MultiEdit_KeepsContextLinesOnSamples_ForTruncatedEdits()
    {
        var resource = new ResourceKey("multi.md");
        var path = Path.Combine(_tempFolder, "multi.md");
        await File.WriteAllLinesAsync(path, new[] { "Y", "Y", "Y", "Y", "Y", "Y", "Y", "Y" });

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
        var root = ParseResult(await tools.MultiEdit("multi.md", editsJson));

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
        var result = await tools.Grep(pattern: "needle", files: "workspace/*.cs");

        result.IsError.Should().BeTrue();
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        text.Should().Contain("files takes a JSON array");
        text.Should().Contain("include");
    }

    [Test]
    public async Task Grep_FilesParameter_RejectsMalformedJsonArray_WithExampleShape()
    {
        var tools = new FileTools(_services);
        var result = await tools.Grep(pattern: "needle", files: "[\"folder/a.txt\",");

        result.IsError.Should().BeTrue();
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        text.Should().Contain("files takes a JSON array");
        text.Should().Contain("folder/a.txt");
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

    [Test]
    public async Task Read_MissingFileUnderNonProjectRoot_EmitsCanonicalRootPath()
    {
        // Regression for vr-4: when a resource under a non-project root is missing, the
        // error must echo the canonical "root:path" form so the agent can see which root
        // failed. A bare path is reserved for the project root and is ambiguous otherwise.
        RegisterTempRoot();

        var tools = new FileTools(_services);
        var result = await tools.Read("temp:missing/file.txt");

        result.IsError.Should().BeTrue();
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        text.Should().Contain("temp:missing/file.txt");
        text.Should().NotContain("'missing/file.txt'");
    }

    [Test]
    public async Task Read_MissingFileUnderProjectRoot_EmitsCanonicalRootPath()
    {
        // Counterpart to the temp: test: project-root keys are reported in their canonical
        // "project:" form to match the cascade scanner's tracked-reference literal and to
        // stay symmetric with non-default roots.
        var tools = new FileTools(_services);
        var result = await tools.Read("project:Scripts/missing.py");

        result.IsError.Should().BeTrue();
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        text.Should().Contain("project:Scripts/missing.py");
    }

    // The file_* tools accept .cel targets — the structured data_* tools are
    // the ergonomic path for routine field/tag mutation, but the byte-write
    // surface stays open for bulk seeding and broken-sidecar repair (the data
    // layer refuses to mutate a Broken sidecar, so file_write is the only
    // repair path).
    [Test]
    public async Task Write_DispatchesCommand_ForCelTarget()
    {
        var dispatched = false;
        _commandService
            .ExecuteAsync<IWriteFileCommand>(
                Arg.Any<Action<IWriteFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                dispatched = true;
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new FileTools(_services);
        var result = await tools.Write("notes/photo.png.cel", "title = \"hello\"\n");

        result.IsError.Should().NotBe(true);
        dispatched.Should().BeTrue();
    }

    [Test]
    public async Task WriteBinary_DispatchesCommand_ForCelTarget()
    {
        var dispatched = false;
        _commandService
            .ExecuteAsync<IWriteBinaryFileCommand>(
                Arg.Any<Action<IWriteBinaryFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                dispatched = true;
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new FileTools(_services);
        var result = await tools.WriteBinary("notes/photo.png.cel", "aGVsbG8=");

        result.IsError.Should().NotBe(true);
        dispatched.Should().BeTrue();
    }

    [Test]
    public async Task Edit_DispatchesCommand_ForCelTarget()
    {
        var dispatched = false;
        _commandService
            .ExecuteAsync<IEditFileCommand, EditFileResult>(
                Arg.Any<Action<IEditFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                dispatched = true;
                return Task.FromResult(Celbridge.Core.Result<EditFileResult>.Ok(new EditFileResult(0, new List<FileEditAffectedRange>(), false)));
            });

        var tools = new FileTools(_services);
        var result = await tools.Edit("notes/photo.png.cel", "old", "new");

        result.IsError.Should().NotBe(true);
        dispatched.Should().BeTrue();
    }

    [Test]
    public async Task MultiEdit_DispatchesCommand_ForCelTarget()
    {
        var dispatched = false;
        _commandService
            .ExecuteAsync<IMultiEditFileCommand, MultiEditFileResult>(
                Arg.Any<Action<IMultiEditFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                dispatched = true;
                return Task.FromResult(Celbridge.Core.Result<MultiEditFileResult>.Ok(
                    new MultiEditFileResult(0, new List<MultiEditFileEditSummary>(), new List<MultiEditFileAffectedRange>())));
            });

        var tools = new FileTools(_services);
        var editsJson = "[{\"oldString\":\"a\",\"newString\":\"b\"}]";
        var result = await tools.MultiEdit("notes/photo.png.cel", editsJson);

        result.IsError.Should().NotBe(true);
        dispatched.Should().BeTrue();
    }

    [Test]
    public async Task Replace_DispatchesCommand_ForCelTarget()
    {
        var dispatched = false;
        _commandService
            .ExecuteAsync<IReplaceFileCommand, ReplaceFileResult>(
                Arg.Any<Action<IReplaceFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                dispatched = true;
                return Task.FromResult(Celbridge.Core.Result<ReplaceFileResult>.Ok(
                    new ReplaceFileResult(0, new List<FileEditAffectedRange>(), false)));
            });

        var tools = new FileTools(_services);
        var result = await tools.Replace("notes/photo.png.cel", "old", "new");

        result.IsError.Should().NotBe(true);
        dispatched.Should().BeTrue();
    }

    [Test]
    public async Task Write_DispatchesCommand_ForNonCelTarget()
    {
        var dispatched = false;
        _commandService
            .ExecuteAsync<IWriteFileCommand>(
                Arg.Any<Action<IWriteFileCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(callInfo =>
            {
                dispatched = true;
                return Task.FromResult(Celbridge.Core.Result.Ok());
            });

        var tools = new FileTools(_services);
        await tools.Write("notes/photo.cel.bak", "hello");

        dispatched.Should().BeTrue();
    }

    [Test]
    public async Task Grep_NonDefaultRootScope_WalksGatewayAndFindsMatches()
    {
        // A temp:-scoped grep walks the named root through the gateway and finds the matches.
        var stagingDir = Path.Combine(_tempFolder, "staging", "sample-package", "src");
        Directory.CreateDirectory(stagingDir);
        await File.WriteAllTextAsync(Path.Combine(stagingDir, "app.js"), "const alpha = 'beta';\n");

        RegisterTempRoot();

        var tools = new FileTools(_services);
        var result = await tools.Grep(pattern: "alpha|beta", path: "temp:staging/sample-package");

        result.IsError.Should().NotBe(true);
        var root = ParseResult(result);
        root.GetProperty("totalMatches").GetInt32().Should().Be(2);
        root.GetProperty("totalFiles").GetInt32().Should().Be(1);
        root.GetProperty("files")[0].GetProperty("resource").GetString()
            .Should().Be("temp:staging/sample-package/src/app.js");
    }

    [Test]
    public async Task Grep_NonDefaultRootFileScope_SearchesThatFile()
    {
        var stagingDir = Path.Combine(_tempFolder, "staging", "sample-package", "src");
        Directory.CreateDirectory(stagingDir);
        await File.WriteAllTextAsync(Path.Combine(stagingDir, "app.js"), "const alpha = 'beta';\n");
        await File.WriteAllTextAsync(Path.Combine(stagingDir, "other.js"), "const alpha = 'gamma';\n");

        RegisterTempRoot();

        var tools = new FileTools(_services);
        var result = await tools.Grep(pattern: "alpha", path: "temp:staging/sample-package/src/app.js");

        result.IsError.Should().NotBe(true);
        var root = ParseResult(result);
        root.GetProperty("totalFiles").GetInt32().Should().Be(1);
        root.GetProperty("files")[0].GetProperty("resource").GetString()
            .Should().Be("temp:staging/sample-package/src/app.js");
    }

    [Test]
    public async Task Grep_NonDefaultRootScope_HonoursIncludeFilter()
    {
        var stagingDir = Path.Combine(_tempFolder, "staging", "sample-package", "src");
        Directory.CreateDirectory(stagingDir);
        await File.WriteAllTextAsync(Path.Combine(stagingDir, "app.js"), "const alpha = 'beta';\n");

        RegisterTempRoot();

        var tools = new FileTools(_services);
        // The only matching file is a .js, so an include of *.css must exclude it.
        var result = await tools.Grep(pattern: "alpha", path: "temp:staging/sample-package", include: "*.css");

        result.IsError.Should().NotBe(true);
        var root = ParseResult(result);
        root.GetProperty("totalMatches").GetInt32().Should().Be(0);
        root.GetProperty("totalFiles").GetInt32().Should().Be(0);
        root.GetProperty("warning").GetString().Should().Contain("No file matched the include patterns '*.css'");
    }

    [Test]
    public async Task Grep_NonDefaultRootScope_IncludeIsRelativeToTheRoot()
    {
        var stagingDir = Path.Combine(_tempFolder, "staging", "sample-package", "src");
        Directory.CreateDirectory(stagingDir);
        await File.WriteAllTextAsync(Path.Combine(stagingDir, "app.js"), "const alpha = 'beta';\n");
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "staging", "sample-package", "top.js"), "alpha\n");

        RegisterTempRoot();

        var tools = new FileTools(_services);
        var result = await tools.Grep(pattern: "alpha", path: "temp:staging/sample-package", include: "staging/sample-package/src/**");

        var root = ParseResult(result);
        root.GetProperty("totalFiles").GetInt32().Should().Be(1);
        root.GetProperty("files")[0].GetProperty("resource").GetString()
            .Should().Be("temp:staging/sample-package/src/app.js");
        root.TryGetProperty("warning", out _).Should().BeFalse();
    }

    [Test]
    public async Task Grep_NonDefaultRootScope_ReadsThePatternAsARegexByDefault()
    {
        var stagingDir = Path.Combine(_tempFolder, "staging");
        Directory.CreateDirectory(stagingDir);
        await File.WriteAllTextAsync(Path.Combine(stagingDir, "notes.txt"), "item 1\nitem 22\nitems\n");

        RegisterTempRoot();

        var tools = new FileTools(_services);
        var regexResult = await tools.Grep(pattern: @"item \d+", path: "temp:staging");
        var literalResult = await tools.Grep(pattern: @"item \d+", literal: true, path: "temp:staging");

        ParseResult(regexResult).GetProperty("totalMatches").GetInt32().Should().Be(2);
        ParseResult(literalResult).GetProperty("totalMatches").GetInt32().Should().Be(0);
    }

    [Test]
    public async Task Grep_MatchesLetterCaseByDefault()
    {
        var stagingDir = Path.Combine(_tempFolder, "staging");
        Directory.CreateDirectory(stagingDir);
        await File.WriteAllTextAsync(Path.Combine(stagingDir, "notes.txt"), "Hello\nhello\n");

        RegisterTempRoot();

        var tools = new FileTools(_services);
        var exactResult = await tools.Grep(pattern: "hello", path: "temp:staging");
        var ignoringResult = await tools.Grep(pattern: "hello", matchCase: false, path: "temp:staging");

        ParseResult(exactResult).GetProperty("totalMatches").GetInt32().Should().Be(1);
        ParseResult(ignoringResult).GetProperty("totalMatches").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task Grep_MissingPath_FailsTheCall()
    {
        Directory.CreateDirectory(Path.Combine(_tempFolder, "staging"));
        RegisterTempRoot();

        var tools = new FileTools(_services);
        var result = await tools.Grep(pattern: "alpha", path: "temp:stagin", include: "*.js");

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Contain("Resource not found: 'temp:stagin'");
    }

    [Test]
    public async Task Grep_RefusedPattern_FailsTheCallWithTheReason()
    {
        var tools = new FileTools(_services);
        var result = await tools.Grep(pattern: "a(?=b)");

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Contain("Lookaround");
    }

    [Test]
    public async Task Grep_RefusedFilter_FailsTheCallWithTheReason()
    {
        var tools = new FileTools(_services);
        var result = await tools.Grep(pattern: "alpha", exclude: "!*.js");

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Contain("starts with '!'");
    }

    [Test]
    public async Task Replace_RefusedReplacement_FailsTheCallWithTheReason()
    {
        var tools = new FileTools(_services);
        var result = await tools.Replace("find.md", "(a)", "$1st", literal: false);

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Single().Text.Should().Contain("${1}st");
    }

    [Test]
    public async Task Search_MatchesTheWalkedFiles_AndLeavesOutFolders()
    {
        UseWalkedItems(
            WalkedFolder("docs"),
            WalkedFile("docs/notes.md"),
            WalkedFile("readme.md"),
            WalkedFile("src/app.py"));

        var tools = new FileTools(_services);
        var result = await tools.Search("*.md");

        ParseStringList(result).Should().Equal("project:docs/notes.md", "project:readme.md");
    }

    [Test]
    public async Task Search_TypeFolder_FindsTheWalkedFolders()
    {
        UseWalkedItems(
            WalkedFolder("docs"),
            WalkedFolder("docs/images"),
            WalkedFile("docs.md"));

        var tools = new FileTools(_services);
        var result = await tools.Search("doc*", type: "folder");

        // A matched folder takes everything inside it, so the folders inside docs match too.
        ParseStringList(result).Should().Equal("project:docs", "project:docs/images");
    }

    [Test]
    public async Task Search_IncludeMetadata_ReportsTheWalkedSizeAndModifiedTime()
    {
        UseWalkedItems(WalkedFile("notes.md", size: 42));

        var tools = new FileTools(_services);
        var result = await tools.Search("notes.md", includeMetadata: true);

        var entry = ParseResult(result)[0];
        entry.GetProperty("resource").GetString().Should().Be("project:notes.md");
        entry.GetProperty("size").GetInt64().Should().Be(42);
        entry.GetProperty("modified").GetString().Should().StartWith("2026-10-19T12:00:00");
    }

    [Test]
    public async Task Search_PatternWithTheProjectPrefix_MatchesTheProjectsPaths()
    {
        UseWalkedItems(
            WalkedFile("docs/notes.md"),
            WalkedFile("readme.md"),
            WalkedFile("src/app.py"));

        var tools = new FileTools(_services);
        var result = await tools.Search("project:*.md");

        ParseStringList(result).Should().Equal("project:docs/notes.md", "project:readme.md");
    }

    [Test]
    public async Task Search_PatternWithASlash_IsRelativeToTheRoot()
    {
        UseWalkedItems(
            WalkedFile("src/app.cs"),
            WalkedFile("src/lib/util.cs"),
            WalkedFile("test/src/other.cs"));

        var tools = new FileTools(_services);
        var result = await tools.Search("src/**/*.cs");

        ParseStringList(result).Should().Equal("project:src/app.cs", "project:src/lib/util.cs");
    }

    [Test]
    public async Task Search_Braces_ExpandToEachAlternative()
    {
        UseWalkedItems(
            WalkedFile("app.ts"),
            WalkedFile("app.tsx"),
            WalkedFile("app.js"));

        var tools = new FileTools(_services);
        var result = await tools.Search("*.{ts,tsx}");

        ParseStringList(result).Should().Equal("project:app.ts", "project:app.tsx");
    }

    [Test]
    public async Task Search_PatternWithAnotherRoot_WalksThatRoot()
    {
        _rootHandlers.RegisterRootHandler(new LogsRootHandler(_tempFolder));
        UseWalkedItems(WalkedFile("logs:app.log"));

        var tools = new FileTools(_services);
        var result = await tools.Search("logs:*.log");

        ParseStringList(result).Should().Equal("logs:app.log");
        await _resourceService.Walker.Received(1).WalkAsync(new ResourceKey("logs:"), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Search_AppliesTheSearchList_UnlessApplySearchExcludeIsOff()
    {
        UseWalkedItems(WalkedFile("readme.md"));

        var tools = new FileTools(_services);
        await tools.Search("*.md");
        await tools.Search("*.md", applySearchExclude: false);

        await _resourceService.Walker.Received(1).WalkAsync(ResourceKey.Empty, true, Arg.Any<CancellationToken>());
        await _resourceService.Walker.Received(1).WalkAsync(ResourceKey.Empty, false, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Grep_PassesTheCompiledPatternFilterAndFlagToTheSearch()
    {
        SearchOptions? searchOptions = null;
        var searchService = Substitute.For<ISearchService>();
        searchService.SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
        {
            searchOptions = callInfo.Arg<SearchOptions>();
            var noFiles = new List<SearchFileResult>();
            return Task.FromResult(new SearchResults("alpha", noFiles, 0, 0, false, false, 0));
        });
        var workspaceWrapper = _services.GetRequiredService<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.SearchService.Returns(searchService);

        var tools = new FileTools(_services);
        await tools.Grep(pattern: "alpha", include: "*.md", applySearchExclude: false);

        searchOptions.Should().NotBeNull();
        searchOptions!.Matcher.Pattern.Should().Be("alpha");
        searchOptions.Filter.IsMatch("docs/notes.md", isFolder: false).Should().BeTrue();
        searchOptions.Filter.IsMatch("docs/notes.txt", isFolder: false).Should().BeFalse();
        searchOptions.ApplySearchExclude.Should().BeFalse();
    }

    // Backs the temp: root with the test folder, so a temp: key resolves to the same file as the project key with
    // the same path.
    private void RegisterTempRoot()
    {
        _rootHandlers.RegisterRootHandler(new TempRootHandler(_tempFolder));
    }

    private void UseWalkedItems(params WalkedItem[] items)
    {
        IReadOnlyList<WalkedItem> walkedItems = items;
        var walker = Substitute.For<IResourceWalker>();
        walker.WalkAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<IReadOnlyList<WalkedItem>>.Ok(walkedItems)));
        _resourceService.Walker.Returns(walker);
    }

    private static WalkedItem WalkedFile(string key, long size = 1)
    {
        var modified = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        return new WalkedItem(new ResourceKey(key), "/" + key, false, size, modified, FileSystemAttributes.None);
    }

    private static WalkedItem WalkedFolder(string key)
    {
        var modified = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        return new WalkedItem(new ResourceKey(key), "/" + key, true, 0, modified, FileSystemAttributes.None);
    }

    private static List<string> ParseStringList(CallToolResult result)
    {
        var root = ParseResult(result);
        return root.EnumerateArray().Select(element => element.GetString()!).ToList();
    }

    private static JsonElement ParseResult(CallToolResult result)
    {
        var json = result.Content.OfType<TextContentBlock>().Single().Text;
        return JsonDocument.Parse(json).RootElement;
    }
}
