using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Resources;
using Celbridge.Resources.Helpers;
using Celbridge.Resources.Services;
using Celbridge.Resources.Services.Roots;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ResourceOperationService — covers the batch property that a
/// batch failing mid-way still commits the prior-successful operations,
/// and a single UndoAsync reverses them cleanly.
/// </summary>
[TestFixture]
public class ResourceOperationServiceTests
{
    private string _testFolder = null!;
    private string _tempFolder = null!;
    private string _dataFolder = null!;
    private string _tempRootFolder = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IResourceService _resourceService = null!;
    private LocalResourceFileSystem _resourceFileSystem = null!;
    private TrashService _trashService = null!;
    private ResourceOperationService _operationService = null!;
    private IProjectService _projectService = null!;

    [SetUp]
    public void Setup()
    {
        _testFolder = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceOperationServiceTests),
            Guid.NewGuid().ToString("N"));

        // _tempFolder is the project folder. In the application, the data folder is outside it.
        _tempFolder = Path.Combine(_testFolder, "Project");
        _dataFolder = Path.Combine(_testFolder, "Data");
        _tempRootFolder = Path.Combine(_dataFolder, ProjectConstants.TempFolder);
        Directory.CreateDirectory(_tempFolder);
        Directory.CreateDirectory(_tempRootFolder);

        var rootHandlerRegistry = TestRootHandlers.Create(_tempFolder);
        rootHandlerRegistry.RegisterRootHandler(new TempRootHandler(_tempRootFolder));

        var resourceService = Substitute.For<IResourceService>();
        _resourceService = resourceService;
        resourceService.RootHandlers.Returns(rootHandlerRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
        _workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        var sidecarService = new SidecarService(_workspaceWrapper);
        resourceService.Sidecars.Returns(sidecarService);

        _resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(_resourceFileSystem);

        var walker = new ResourceWalker(Substitute.For<ILogger<ResourceWalker>>(), _workspaceWrapper);
        resourceService.Walker.Returns(walker);

        _projectService = Substitute.For<IProjectService>();
        StubCurrentProject("Acme.celbridge");

        _trashService = new TrashService(
            Substitute.For<ILogger<TrashService>>(),
            _workspaceWrapper,
            _projectService,
            TestFileSystem.CreateLocal());
        resourceService.Trash.Returns(_trashService);

        _operationService = new ResourceOperationService(
            Substitute.For<ILogger<ResourceOperationService>>(),
            _workspaceWrapper,
            _projectService,
            TestFileSystem.CreateLocal());
    }

    [Test]
    public async Task MovingTheProjectFileOutOfItsFolder_IsRefused()
    {
        StubCurrentProject("Acme.celbridge");

        var result = await _operationService.MoveAsync(
            new ResourceKey("Acme.celbridge"),
            new ResourceKey("sub/Acme.celbridge"));

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("cannot be moved out of it");
    }

    [Test]
    public async Task RenamingTheProjectFileInPlace_IsNotRefused()
    {
        // The guard lets the rename through, so the move then fails on the missing source. Asserting on
        // which failure came back is what distinguishes "allowed" from "refused" without staging files.
        StubCurrentProject("Acme.celbridge");

        var result = await _operationService.MoveAsync(
            new ResourceKey("Acme.celbridge"),
            new ResourceKey("Renamed.celbridge"));

        result.FirstErrorMessage.Should().NotContain("Move the whole folder instead");
    }

    [Test]
    public async Task MovingAnOrdinaryFile_IsNotRefused()
    {
        StubCurrentProject("Acme.celbridge");

        var result = await _operationService.MoveAsync(
            new ResourceKey("notes.md"),
            new ResourceKey("sub/notes.md"));

        result.FirstErrorMessage.Should().NotContain("Move the whole folder instead");
    }

    [Test]
    public async Task RenamingTheProjectFileToAnotherExtension_IsRefused()
    {
        StubCurrentProject("Acme.celbridge");

        var result = await _operationService.MoveAsync(
            new ResourceKey("Acme.celbridge"),
            new ResourceKey("Acme.txt"));

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("must keep its .celbridge extension");
    }

    // A real Project rather than a substitute, so the guard runs the actual IsProjectFile comparison
    // instead of a stub's default answer.
    private void StubCurrentProject(string projectFileName)
    {
        var projectFolderPath = _tempFolder;

        var project = new Project(
            Path.Combine(projectFolderPath, projectFileName),
            Path.GetFileNameWithoutExtension(projectFileName),
            projectFolderPath,
            _dataFolder,
            new ProjectConfig(),
            MigrationResult.Success(),
            ConfigIsHealthy: true,
            ConfigLoadFailure: null);

        _projectService.CurrentProject.Returns(project);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testFolder))
        {
            Directory.Delete(_testFolder, true);
        }
    }

    [Test]
    public async Task PartialBatch_FailureMidway_CommitsPriorOps_AndSingleUndoReversesThem()
    {
        // Pre-create a file outside the batch so the second CreateFileAsync
        // inside the batch fails with "Resource already exists".
        var existingPath = Path.Combine(_tempFolder, "existing.txt");
        await File.WriteAllTextAsync(existingPath, "preexisting");

        var newResource = new ResourceKey("new.txt");
        var existingResource = new ResourceKey("existing.txt");
        var newPath = Path.Combine(_tempFolder, "new.txt");

        using (var batch = _operationService.BeginBatch())
        {
            var firstCreate = await _operationService.CreateFileAsync(newResource, new byte[] { 0x01, 0x02 });
            firstCreate.IsSuccess.Should().BeTrue();

            // The second op fails inside CreateOperation.ExecuteAsync (the
            // probe sees the existing file) and is NOT added to the batch.
            var secondCreate = await _operationService.CreateFileAsync(existingResource, new byte[] { 0xFF });
            secondCreate.IsFailure.Should().BeTrue();
        }

        // After the using-block commits the partial batch: the newly-created
        // file is on disk and the pre-existing file is untouched.
        File.Exists(newPath).Should().BeTrue();
        File.Exists(existingPath).Should().BeTrue();
        (await File.ReadAllTextAsync(existingPath)).Should().Be("preexisting");

        // A single UndoAsync reverses the committed partial batch: the new
        // file is deleted; the pre-existing file (never inside the batch) stays.
        _operationService.CanUndo.Should().BeTrue();
        var undoResult = await _operationService.UndoAsync();
        undoResult.IsSuccess.Should().BeTrue();

        File.Exists(newPath).Should().BeFalse();
        File.Exists(existingPath).Should().BeTrue();
        _operationService.CanUndo.Should().BeFalse();
        _operationService.CanRedo.Should().BeTrue();
    }

    [Test]
    public async Task EmptyBatch_DoesNotPushAnUndoEntry()
    {
        using (var batch = _operationService.BeginBatch())
        {
            // No operations queued.
        }

        // An empty batch is discarded — CanUndo stays false.
        _operationService.CanUndo.Should().BeFalse();
    }

    [Test]
    public async Task CommittedBatches_StayWithinTheUndoLimit()
    {
        // One batch more than the undo stack contains. The oldest batch leaves the stack, so its file stays once
        // every remaining batch is undone.
        var batchCount = ResourceOperationService.MaxUndoStackSize + 1;
        for (var i = 0; i < batchCount; i++)
        {
            using var batch = _operationService.BeginBatch();
            var createResult = await _operationService.CreateFileAsync(new ResourceKey($"file{i}.txt"), [0x01]);
            createResult.IsSuccess.Should().BeTrue();
        }

        var undoCount = 0;
        while (_operationService.CanUndo)
        {
            var undoResult = await _operationService.UndoAsync();
            undoResult.IsSuccess.Should().BeTrue();
            undoCount++;
        }

        undoCount.Should().Be(ResourceOperationService.MaxUndoStackSize);
        File.Exists(Path.Combine(_tempFolder, "file0.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "file1.txt")).Should().BeFalse();
    }

    [Test]
    public async Task BatchScope_CommitsOnDispose_EvenWhenReturnExitsEarly()
    {
        var firstResource = new ResourceKey("a.txt");
        var secondResource = new ResourceKey("b.txt");

        // Wrap in a local async function that returns early from inside the
        // using block; the BatchScope's Dispose must still commit on the way out.
        async Task<bool> RunPartialBatch()
        {
            using var batch = _operationService.BeginBatch();
            var first = await _operationService.CreateFileAsync(firstResource, new byte[] { 0x01 });
            if (first.IsFailure)
            {
                return false;
            }

            // Early return mid-batch — the second CreateFileAsync never runs.
            return true;
        }

        var ran = await RunPartialBatch();
        ran.Should().BeTrue();

        File.Exists(Path.Combine(_tempFolder, "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "b.txt")).Should().BeFalse();

        // The partially-populated batch did commit — UndoAsync reverses the
        // single create.
        _operationService.CanUndo.Should().BeTrue();
        var undoResult = await _operationService.UndoAsync();
        undoResult.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "a.txt")).Should().BeFalse();
    }

    [Test]
    public async Task UndoCreate_FileEditedAfterwards_FailsAndKeepsTheFile()
    {
        var path = await CreateProjectFileAsync("new.py", "original");
        await File.WriteAllTextAsync(path, "an hour of work");

        var firstUndo = await _operationService.UndoAsync();
        var secondUndo = await _operationService.UndoAsync();

        firstUndo.IsFailure.Should().BeTrue();
        firstUndo.FirstErrorMessage.Should().Contain("new.py");
        secondUndo.IsFailure.Should().BeTrue();
        secondUndo.FirstErrorMessage.Should().Be(firstUndo.FirstErrorMessage);
        (await File.ReadAllTextAsync(path)).Should().Be("an hour of work");
        _operationService.CanUndo.Should().BeTrue();
        _operationService.CanRedo.Should().BeFalse();
    }

    [Test]
    public async Task UndoCreate_FileEditedToTheSameSize_FailsAndKeepsTheFile()
    {
        var path = await CreateProjectFileAsync("new.py", "aaaa");
        await File.WriteAllTextAsync(path, "bbbb");

        // Two quick writes can share a clock tick, so the test moves the modified time on.
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(1));

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsFailure.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("bbbb");
    }

    [Test]
    public async Task UndoCreate_UnchangedFile_RemovesTheFileAndRedoRestoresTheBytes()
    {
        var path = await CreateProjectFileAsync("new.py", "original");

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsSuccess.Should().BeTrue(undoResult.MessageChain);
        File.Exists(path).Should().BeFalse();

        var redoResult = await _operationService.RedoAsync();

        redoResult.IsSuccess.Should().BeTrue(redoResult.MessageChain);
        (await File.ReadAllTextAsync(path)).Should().Be("original");
    }

    [Test]
    public async Task UndoCreate_AfterARedo_RemovesTheFile()
    {
        var path = await CreateProjectFileAsync("new.py", "original");
        await _operationService.UndoAsync();
        await _operationService.RedoAsync();

        // Redo gives the file a new modified time and records a new stamp. This undo needs the new stamp.
        var undoResult = await _operationService.UndoAsync();

        undoResult.IsSuccess.Should().BeTrue(undoResult.MessageChain);
        File.Exists(path).Should().BeFalse();
    }

    [Test]
    public async Task UndoCreate_AfterARenameIsUndone_RemovesTheFile()
    {
        var path = await CreateProjectFileAsync("new.py", "original");
        await _operationService.MoveAsync(new ResourceKey("new.py"), new ResourceKey("renamed.py"));

        var undoRename = await _operationService.UndoAsync();
        var undoCreate = await _operationService.UndoAsync();

        undoRename.IsSuccess.Should().BeTrue(undoRename.MessageChain);
        undoCreate.IsSuccess.Should().BeTrue(undoCreate.MessageChain);
        File.Exists(path).Should().BeFalse();
    }

    [Test]
    public async Task UndoCreate_FileEditedAfterARename_FailsOnTheSecondUndo()
    {
        var path = await CreateProjectFileAsync("new.py", "original");
        await _operationService.MoveAsync(new ResourceKey("new.py"), new ResourceKey("renamed.py"));
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "renamed.py"), "an hour of work");

        var undoRename = await _operationService.UndoAsync();
        var undoCreate = await _operationService.UndoAsync();

        undoRename.IsSuccess.Should().BeTrue(undoRename.MessageChain);
        undoCreate.IsFailure.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("an hour of work");
    }

    [Test]
    public async Task UndoCreate_AfterADeleteIsUndone_RemovesTheFile()
    {
        var path = await CreateProjectFileAsync("new.py", "original");
        await _operationService.DeleteAsync(new ResourceKey("new.py"));

        var undoDelete = await _operationService.UndoAsync();

        undoDelete.IsSuccess.Should().BeTrue(undoDelete.MessageChain);
        File.Exists(path).Should().BeTrue();

        var undoCreate = await _operationService.UndoAsync();

        undoCreate.IsSuccess.Should().BeTrue(undoCreate.MessageChain);
        File.Exists(path).Should().BeFalse();
    }

    [Test]
    public async Task UndoCreate_FolderThatIsNotEmpty_SucceedsAndLeavesTheFolder()
    {
        await _operationService.CreateFolderAsync(new ResourceKey("folder"));
        var filePath = Path.Combine(_tempFolder, "folder", "inside.txt");
        await File.WriteAllTextAsync(filePath, "inside");

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsSuccess.Should().BeTrue(undoResult.MessageChain);
        (await File.ReadAllTextAsync(filePath)).Should().Be("inside");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UndoCopyOrImport_FileEditedAfterwards_FailsAndKeepsTheFile(bool isImport)
    {
        var path = await AddFileAsync(isImport, "added.txt");
        await File.WriteAllTextAsync(path, "an hour of work");

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsFailure.Should().BeTrue();
        undoResult.FirstErrorMessage.Should().Contain("added.txt");
        (await File.ReadAllTextAsync(path)).Should().Be("an hour of work");
    }

    [Test]
    public async Task UndoCopy_SourceEditedAfterwards_FailsAndKeepsTheCopy()
    {
        var sourcePath = Path.Combine(_tempFolder, "source.txt");
        await File.WriteAllTextAsync(sourcePath, "original");
        await _operationService.CopyAsync(new ResourceKey("source.txt"), new ResourceKey("backup.txt"));
        await File.WriteAllTextAsync(sourcePath, "an hour of work");

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsFailure.Should().BeTrue();
        undoResult.FirstErrorMessage.Should().Contain("backup.txt");
        (await File.ReadAllTextAsync(Path.Combine(_tempFolder, "backup.txt"))).Should().Be("original");
    }

    [Test]
    public async Task UndoImport_SourceDeletedAfterwards_FailsAndKeepsTheFile()
    {
        var sourcePath = Path.Combine(_testFolder, "source.txt");
        await File.WriteAllTextAsync(sourcePath, "original");
        await _operationService.ImportExternalFileAsync(sourcePath, new ResourceKey("imported.txt"));
        File.Delete(sourcePath);

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsFailure.Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(_tempFolder, "imported.txt"))).Should().Be("original");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UndoCopyOrImport_FolderWithAFileEdited_FailsAndKeepsTheFolder(bool isImport)
    {
        var folderPath = await AddFolderAsync(isImport);
        var filePath = Path.Combine(folderPath, "sub", "inside.txt");
        await File.WriteAllTextAsync(filePath, "an hour of work");

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsFailure.Should().BeTrue();
        (await File.ReadAllTextAsync(filePath)).Should().Be("an hour of work");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UndoCopyOrImport_FolderWithAFileAdded_FailsAndKeepsTheFolder(bool isImport)
    {
        var folderPath = await AddFolderAsync(isImport);
        var filePath = Path.Combine(folderPath, "new.txt");
        await File.WriteAllTextAsync(filePath, "new");

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsFailure.Should().BeTrue();
        File.Exists(filePath).Should().BeTrue();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UndoCopyOrImport_UnchangedFolder_RemovesTheFolder(bool isImport)
    {
        var folderPath = await AddFolderAsync(isImport);

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsSuccess.Should().BeTrue(undoResult.MessageChain);
        Directory.Exists(folderPath).Should().BeFalse();
    }

    [Test]
    public async Task UndoImport_SourceFolderWithAFileEdited_FailsAndKeepsTheFolder()
    {
        var folderPath = await AddFolderAsync(isImport: true);
        await File.WriteAllTextAsync(Path.Combine(_testFolder, "SourceFolder", "sub", "inside.txt"), "an hour of work");

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsFailure.Should().BeTrue();
        Directory.Exists(folderPath).Should().BeTrue();
    }

    [Test]
    public async Task UndoCopy_FolderThatCannotBeWalked_FailsAndKeepsTheFolder()
    {
        // The copy operation stores this walker, so the test changes the walker's result after the copy.
        var realWalker = _resourceService.Walker;
        var walker = Substitute.For<IResourceWalker>();
        walker.WalkAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<IPathMatcher?>(), Arg.Any<CancellationToken>())
            .Returns(call => realWalker.WalkAsync(call.ArgAt<ResourceKey>(0), call.ArgAt<bool>(1)));
        _resourceService.Walker.Returns(walker);

        var folderPath = await AddFolderAsync(isImport: false);

        var failedWalk = Result<IReadOnlyList<WalkedItem>>.Fail("The folder could not be listed");
        walker.WalkAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<IPathMatcher?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(failedWalk));

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsFailure.Should().BeTrue();
        Directory.Exists(folderPath).Should().BeTrue();
    }

    [Test]
    public async Task UndoCopy_FileDeletedOutsideTheUndoStack_Succeeds()
    {
        var path = await AddFileAsync(isImport: false, "added.txt");
        File.Delete(path);

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsSuccess.Should().BeTrue(undoResult.MessageChain);
        _operationService.CanUndo.Should().BeFalse();
    }

    [Test]
    public async Task UndoBatch_WithOneChangedFile_RemovesNothingAndKeepsTheEntry()
    {
        using (var batch = _operationService.BeginBatch())
        {
            await _operationService.CreateFileAsync(new ResourceKey("first.txt"), new byte[] { 0x01 });
            await _operationService.CreateFileAsync(new ResourceKey("second.txt"), new byte[] { 0x02 });
            await _operationService.CreateFileAsync(new ResourceKey("third.txt"), new byte[] { 0x03 });
        }
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "second.txt"), "an hour of work");

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsFailure.Should().BeTrue();
        undoResult.FirstErrorMessage.Should().Contain("second.txt");
        File.Exists(Path.Combine(_tempFolder, "first.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "second.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "third.txt")).Should().BeTrue();
        _operationService.CanUndo.Should().BeTrue();
    }

    [Test]
    public async Task DeleteAsync_UnderTheProjectRoot_CanBeUndone()
    {
        var path = Path.Combine(_tempFolder, "notes.txt");
        await File.WriteAllTextAsync(path, "notes");

        var deleteResult = await _operationService.DeleteAsync(new ResourceKey("notes.txt"));

        deleteResult.IsSuccess.Should().BeTrue(deleteResult.MessageChain);
        deleteResult.Value.Should().BeTrue();
        File.Exists(path).Should().BeFalse();

        var undoResult = await _operationService.UndoAsync();

        undoResult.IsSuccess.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("notes");
    }

    [Test]
    public async Task DeleteAsync_OutsideTheProjectRoot_DeletesWithoutUndo()
    {
        var path = Path.Combine(_tempRootFolder, "scratch.txt");
        await File.WriteAllTextAsync(path, "scratch");

        var deleteResult = await _operationService.DeleteAsync(new ResourceKey("temp:scratch.txt"));

        deleteResult.IsSuccess.Should().BeTrue(deleteResult.MessageChain);
        deleteResult.Value.Should().BeFalse();
        File.Exists(path).Should().BeFalse();
        _operationService.CanUndo.Should().BeFalse();
        Directory.Exists(Path.Combine(_dataFolder, "trash")).Should().BeFalse("the delete skips the trash");
    }

    [Test]
    public async Task DeleteAsync_FolderThatContainsALink_DeletesWithoutUndo()
    {
        var targetPath = Path.Combine(_tempFolder, "target.txt");
        await File.WriteAllTextAsync(targetPath, "target");
        var folderPath = Path.Combine(_tempFolder, "folder");
        Directory.CreateDirectory(folderPath);
        await File.WriteAllTextAsync(Path.Combine(folderPath, "plain.txt"), "plain");
        File.CreateSymbolicLink(Path.Combine(folderPath, "link.txt"), targetPath);

        var deleteResult = await _operationService.DeleteAsync(new ResourceKey("folder"));

        deleteResult.IsSuccess.Should().BeTrue(deleteResult.MessageChain);
        deleteResult.Value.Should().BeFalse();
        Directory.Exists(folderPath).Should().BeFalse();
        (await File.ReadAllTextAsync(targetPath)).Should().Be("target", "deleting a link leaves what it points at");
        _operationService.CanUndo.Should().BeFalse();
    }

    [Test]
    public async Task DeleteAsync_FolderThatContainsALink_ClearsTheRedoStack()
    {
        var notesPath = Path.Combine(_tempFolder, "notes.txt");
        await File.WriteAllTextAsync(notesPath, "notes");
        await _operationService.DeleteAsync(new ResourceKey("notes.txt"));
        await _operationService.UndoAsync();
        _operationService.CanRedo.Should().BeTrue();

        var folderPath = Path.Combine(_tempFolder, "folder");
        Directory.CreateDirectory(folderPath);
        File.CreateSymbolicLink(Path.Combine(folderPath, "link.txt"), notesPath);

        var deleteResult = await _operationService.DeleteAsync(new ResourceKey("folder"));

        deleteResult.IsSuccess.Should().BeTrue(deleteResult.MessageChain);
        deleteResult.Value.Should().BeFalse();
        _operationService.CanRedo.Should().BeFalse("the delete changed the tree the redo would replay against");
    }

    [Test]
    public async Task EnumerateFolderAsync_LeavesOutLinkedFilesAndFolders()
    {
        CreateFolderThatContainsLinks();

        var enumerateResult = await _resourceFileSystem.EnumerateFolderAsync(new ResourceKey("folder"));

        enumerateResult.IsSuccess.Should().BeTrue(enumerateResult.MessageChain);
        enumerateResult.Value.Select(item => item.Resource.ResourceName).Should().Equal("plain.txt");
    }

    [Test]
    public async Task HasChildAsync_LeavesOutLinkedFilesAndFolders()
    {
        CreateFolderThatContainsLinks();
        var offeredNames = new List<string>();

        var hasChildResult = await _resourceFileSystem.HasChildAsync(new ResourceKey("folder"), folderChild =>
        {
            offeredNames.Add(folderChild.Resource.ResourceName);
            return false;
        });

        hasChildResult.IsSuccess.Should().BeTrue(hasChildResult.MessageChain);
        hasChildResult.Value.Should().BeFalse();
        offeredNames.Should().Equal("plain.txt");
    }

    [Test]
    public async Task HasChildAsync_FailsForAFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "notes.txt"), "notes");

        var hasChildResult = await _resourceFileSystem.HasChildAsync(new ResourceKey("notes.txt"), _ => true);

        hasChildResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task CollectSourceFilesAsync_FolderThatContainsLinks_LeavesThemOutOfTheArchive()
    {
        CreateFolderThatContainsLinks();

        var sourceFiles = await ArchiveHelper.CollectSourceFilesAsync(_resourceFileSystem, new ResourceKey("folder"), isFolder: true);

        sourceFiles.Select(sourceFile => sourceFile.EntryName).Should().Equal("plain.txt");
    }

    [Test]
    public async Task CopyAsync_FolderThatContainsLinks_LeavesThemOutOfTheCopy()
    {
        CreateFolderThatContainsLinks();

        var copyResult = await _operationService.CopyAsync(new ResourceKey("folder"), new ResourceKey("copy"));

        copyResult.IsSuccess.Should().BeTrue(copyResult.MessageChain);
        Directory.EnumerateFileSystemEntries(Path.Combine(_tempFolder, "copy"))
            .Select(Path.GetFileName)
            .Should().Equal("plain.txt");
    }

    [Test]
    public async Task ImportExternalFolderAsync_LeavesOutLinksAndNamesEachOne()
    {
        var sourcePath = CreateOutsideFolderThatContainsLinks();

        var importResult = await _operationService.ImportExternalFolderAsync(sourcePath, new ResourceKey("imported"));

        importResult.IsSuccess.Should().BeTrue(importResult.MessageChain);
        Directory.EnumerateFileSystemEntries(Path.Combine(_tempFolder, "imported"))
            .Select(Path.GetFileName)
            .Should().Equal("plain.txt");

        var leftOutLinks = importResult.Value.LeftOutLinks;
        leftOutLinks.Select(link => link.Resource.Path).Should().BeEquivalentTo("imported/linked", "imported/linked.txt");
        leftOutLinks.Should().OnlyContain(link => link.Reason is LinkNotFollowedError);
    }

    [Test]
    public async Task ImportExternalFileAsync_ALink_IsRefused()
    {
        var sourcePath = CreateOutsideFolderThatContainsLinks();

        var importResult = await _operationService.ImportExternalFileAsync(
            Path.Combine(sourcePath, "linked.txt"), new ResourceKey("imported.txt"));

        importResult.IsFailure.Should().BeTrue();
        importResult.HasException<LinkNotFollowedError>().Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "imported.txt")).Should().BeFalse();
    }

    [Test]
    public async Task ImportExternalFolderAsync_ALinkedFolder_IsRefused()
    {
        var sourcePath = CreateOutsideFolderThatContainsLinks();

        var importResult = await _operationService.ImportExternalFolderAsync(
            Path.Combine(sourcePath, "linked"), new ResourceKey("imported"));

        importResult.IsFailure.Should().BeTrue();
        importResult.HasException<LinkNotFollowedError>().Should().BeTrue();
        Directory.Exists(Path.Combine(_tempFolder, "imported")).Should().BeFalse();
    }

    [Test]
    public async Task CreateAndCopy_OutsideTheProjectRoot_AreNotRecorded()
    {
        var createResult = await _operationService.CreateFileAsync(new ResourceKey("temp:created.txt"), new byte[] { 0x01 });
        createResult.IsSuccess.Should().BeTrue(createResult.MessageChain);

        await File.WriteAllTextAsync(Path.Combine(_tempFolder, "source.txt"), "source");
        var copyResult = await _operationService.CopyAsync(new ResourceKey("source.txt"), new ResourceKey("temp:copied.txt"));
        copyResult.IsSuccess.Should().BeTrue(copyResult.MessageChain);

        File.Exists(Path.Combine(_tempRootFolder, "created.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_tempRootFolder, "copied.txt")).Should().BeTrue();
        _operationService.CanUndo.Should().BeFalse();
    }

    [Test]
    public async Task DeleteAsync_DeniesByPolicy_WhenResourceIsReserved()
    {
        // The trash-based delete bypasses IResourceFileSystem.DeleteAsync, so this
        // guard at the service entry is the load-bearing one for the reserved folders.
        var metadataFolder = Path.Combine(_tempFolder, ".git");
        Directory.CreateDirectory(metadataFolder);

        var deleteResult = await _operationService.DeleteAsync(new ResourceKey(".git"));

        deleteResult.IsFailure.Should().BeTrue();
        deleteResult.HasException<PolicyDenialError>().Should().BeTrue();
        Directory.Exists(metadataFolder).Should().BeTrue();
    }

    [Test]
    public async Task CreateFileAsync_DeniesByPolicy_WhenDestinationIsReserved()
    {
        var result = await _operationService.CreateFileAsync(
            new ResourceKey(".git/config"), new byte[] { 0x01 });

        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, ".git", "config")).Should().BeFalse();
    }

    [Test]
    public async Task CreateFileAsync_Succeeds_WhenDestinationIsHidden()
    {
        // Hiding is cosmetic: a destination the Explorer would not draw is still a
        // destination anything may write to.
        var config = new ProjectConfig
        {
            Explorer = new ExplorerSection { Exclude = new[] { "*.log" } },
        };
        // Built into a local first: BuildPolicy stands up its own substitutes, which would
        // corrupt NSubstitute's last-call context if it ran inside Returns(...).
        var policy = BuildPolicy(config);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var result = await _operationService.CreateFileAsync(new ResourceKey("notes.log"), new byte[] { 0x01 });

        result.IsSuccess.Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "notes.log")).Should().BeTrue();
    }

    [Test]
    public async Task MoveAsync_DeniesByPolicy_WhenSourceIsReserved()
    {
        var metadataFolder = Path.Combine(_tempFolder, ".git");
        Directory.CreateDirectory(metadataFolder);

        var result = await _operationService.MoveAsync(new ResourceKey(".git"), new ResourceKey("Archive"));

        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
        Directory.Exists(Path.Combine(_tempFolder, "Archive")).Should().BeFalse();
    }

    [Test]
    public void CanCreateResource_Fails_WhenDestinationIsReserved()
    {
        var result = _operationService.CanCreateResource(new ResourceKey(".git/config"), isFolder: false);

        result.IsFailure.Should().BeTrue();
        result.HasException<PolicyDenialError>().Should().BeTrue();
    }

    [Test]
    public void CanAddToFolder_Fails_WhenFolderIsReserved()
    {
        var result = _operationService.CanAddToFolder(new ResourceKey(".git"));

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void CanAddToFolder_Succeeds_WhenFolderIsSearchExcluded()
    {
        // The search list binds search, not the operation layer.
        var config = new ProjectConfig
        {
            Search = new SearchSection { Exclude = new[] { "node_modules" } },
        };
        // Built into a local first: BuildPolicy stands up its own substitutes, which would
        // corrupt NSubstitute's last-call context if it ran inside Returns(...).
        var policy = BuildPolicy(config);
        _workspaceWrapper.WorkspaceService.ResourceService.Policy.Returns(policy);

        var result = _operationService.CanAddToFolder(new ResourceKey("node_modules"));

        result.IsSuccess.Should().BeTrue();
    }

    private static ResourcePolicy BuildPolicy(ProjectConfig config)
    {
        var project = Substitute.For<IProject>();
        project.Config.Returns(config);
        project.ProjectFolderPath.Returns(@"C:\fake\project");

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        return new ResourcePolicy(projectService);
    }

    // Creates a file through the operation service. Returns the file's path.
    private async Task<string> CreateProjectFileAsync(string name, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var createResult = await _operationService.CreateFileAsync(new ResourceKey(name), bytes);
        createResult.IsSuccess.Should().BeTrue(createResult.MessageChain);

        return Path.Combine(_tempFolder, name);
    }

    // Adds a file to the project by copying or importing a source file. Returns the added file's path.
    private async Task<string> AddFileAsync(bool isImport, string name)
    {
        Result addResult;
        if (isImport)
        {
            var sourcePath = Path.Combine(_testFolder, "source.txt");
            await File.WriteAllTextAsync(sourcePath, "original");
            addResult = await _operationService.ImportExternalFileAsync(sourcePath, new ResourceKey(name));
        }
        else
        {
            await File.WriteAllTextAsync(Path.Combine(_tempFolder, "source.txt"), "original");
            addResult = await _operationService.CopyAsync(new ResourceKey("source.txt"), new ResourceKey(name));
        }
        addResult.IsSuccess.Should().BeTrue(addResult.MessageChain);

        return Path.Combine(_tempFolder, name);
    }

    // Adds a folder named added to the project by copying or importing a source folder. The source folder contains
    // sub/inside.txt. Returns the added folder's path.
    private async Task<string> AddFolderAsync(bool isImport)
    {
        var sourceParent = isImport
            ? _testFolder
            : _tempFolder;
        var sourcePath = Path.Combine(sourceParent, "SourceFolder");
        Directory.CreateDirectory(Path.Combine(sourcePath, "sub"));
        await File.WriteAllTextAsync(Path.Combine(sourcePath, "sub", "inside.txt"), "original");

        var dest = new ResourceKey("added");
        Result addResult;
        if (isImport)
        {
            addResult = await _operationService.ImportExternalFolderAsync(sourcePath, dest);
        }
        else
        {
            addResult = await _operationService.CopyAsync(new ResourceKey("SourceFolder"), dest);
        }
        addResult.IsSuccess.Should().BeTrue(addResult.MessageChain);

        return Path.Combine(_tempFolder, "added");
    }

    // Creates a folder outside the project containing a plain file, a link to a file and a link to a folder.
    // Returns its path.
    private string CreateOutsideFolderThatContainsLinks()
    {
        var targetFolder = Path.Combine(_testFolder, "Targets");
        Directory.CreateDirectory(targetFolder);
        var secretPath = Path.Combine(targetFolder, "secret.txt");
        File.WriteAllText(secretPath, "secret");

        var sourcePath = Path.Combine(_testFolder, "Source");
        Directory.CreateDirectory(sourcePath);
        File.WriteAllText(Path.Combine(sourcePath, "plain.txt"), "plain");
        File.CreateSymbolicLink(Path.Combine(sourcePath, "linked.txt"), secretPath);
        Directory.CreateSymbolicLink(Path.Combine(sourcePath, "linked"), targetFolder);

        return sourcePath;
    }

    // Creates project:folder containing a plain file, a link to a file outside the project and a link to a
    // folder outside the project.
    private void CreateFolderThatContainsLinks()
    {
        var outsideFolder = Path.Combine(_testFolder, "Outside");
        Directory.CreateDirectory(outsideFolder);
        var secretPath = Path.Combine(outsideFolder, "secret.txt");
        File.WriteAllText(secretPath, "secret");

        var folderPath = Path.Combine(_tempFolder, "folder");
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, "plain.txt"), "plain");
        File.CreateSymbolicLink(Path.Combine(folderPath, "linked.txt"), secretPath);
        Directory.CreateSymbolicLink(Path.Combine(folderPath, "linked"), outsideFolder);
    }
}
