using Celbridge.Automation;
using Celbridge.Explorer;
using Celbridge.Explorer.Models;
using Celbridge.Explorer.Services;
using Celbridge.Messaging;
using Celbridge.Platform;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Tests the Explorer tree's model against a real project folder. The tests cover which folders the model lists and
/// checks as folders open and close, and the expanders within and above the look-ahead limit. They also cover updates
/// after a change and a resync, rows keeping their identity, the view filter, and reveal.
/// </summary>
[TestFixture]
public class ResourceTreeModelTests
{
    private string _projectFolderPath = null!;
    private RootHandlerRegistry _rootHandlers = null!;
    private IResourceService _resourceService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private FakeResourceMonitor _monitor = null!;
    private FolderStateService _folderStateService = null!;
    private IBindableWorkspaceSettings _workspaceSettings = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceTreeModelTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _rootHandlers = TestRootHandlers.Create(_projectFolderPath);
        _monitor = new FakeResourceMonitor();

        _resourceService = Substitute.For<IResourceService>();
        _resourceService.RootHandlers.Returns(_rootHandlers);
        _resourceService.Monitor.Returns(_monitor);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(_resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        _resourceService.FileSystem.Returns(resourceFileSystem);

        UsePolicy(explorerExcludePatterns: [], searchExcludePatterns: []);

        _folderStateService = new FolderStateService(_workspaceWrapper);
        var explorerService = Substitute.For<IExplorerService>();
        explorerService.FolderStateService.Returns(_folderStateService);
        workspaceService.ExplorerService.Returns(explorerService);

        _workspaceSettings = Substitute.For<IBindableWorkspaceSettings>();
        workspaceService.BindableWorkspaceSettings.Returns(_workspaceSettings);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            try
            {
                Directory.Delete(_projectFolderPath, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Test]
    public async Task Refresh_ListsTheProjectFolder_AndChecksEachSubfolder()
    {
        CreateFolder("a");
        WriteFile("a/x.txt");
        CreateFolder("b");
        WriteFile("c.txt");
        var model = CreateModel();

        await model.RefreshAsync();

        RowPaths(model).Should().Equal("", "a", "b", "c.txt");
        model.ListedFolders.Should().BeEquivalentTo(Keys(""));
        model.CheckedFolders.Should().BeEquivalentTo(Keys("a", "b"));
        Row(model, "a").HasChildren.Should().BeTrue();
        Row(model, "b").HasChildren.Should().BeFalse();
    }

    [Test]
    public async Task Refresh_AboveTheLookAheadLimit_ShowsAnExpanderOnEverySubfolder()
    {
        for (var folderNumber = 0; folderNumber < 51; folderNumber++)
        {
            CreateFolder($"folder{folderNumber:D2}");
        }
        var model = CreateModel();

        await model.RefreshAsync();

        model.ListedFolders.Should().BeEquivalentTo(Keys(""));
        model.CheckedFolders.Should().BeEmpty();
        model.Rows
            .Where(row => !row.IsProjectFolder)
            .Should().OnlyContain(row => row.HasChildren);
    }

    [Test]
    public async Task SetExpanded_ListsTheFolderAndChecksItsSubfolders_AndCollapsingChecksTheFolder()
    {
        CreateFolder("a/b/c");
        var model = CreateModel();
        await model.RefreshAsync();

        await model.SetExpandedAsync(ResourceKey.Create("a"), true);

        RowPaths(model).Should().Equal("", "a", "a/b");
        model.ListedFolders.Should().BeEquivalentTo(Keys("", "a"));
        model.CheckedFolders.Should().BeEquivalentTo(Keys("a/b"));
        Row(model, "a/b").HasChildren.Should().BeTrue();

        await model.SetExpandedAsync(ResourceKey.Create("a"), false);

        RowPaths(model).Should().Equal("", "a");
        model.ListedFolders.Should().BeEquivalentTo(Keys(""));
        model.CheckedFolders.Should().BeEquivalentTo(Keys("a"));
    }

    [Test]
    public async Task SetExpanded_KeepsAFolderMadeBeforeItsChangeArrives()
    {
        var model = CreateModel();
        await model.RefreshAsync();

        CreateFolder("fresh");
        WriteFile("fresh/x.txt");
        await model.SetExpandedAsync(ResourceKey.Create("fresh"), true);

        _folderStateService.IsExpanded(ResourceKey.Create("fresh")).Should().BeTrue();
        RowPaths(model).Should().Equal("", "fresh", "fresh/x.txt");
    }

    [Test]
    public async Task ChangeInAListedFolder_RelistsIt_AndKeepsTheUnchangedRows()
    {
        WriteFile("c.txt");
        var model = CreateModel();
        await model.RefreshAsync();
        var unchangedRow = Row(model, "c.txt");
        var rowsChangedCount = 0;
        model.RowsChanged += () => rowsChangedCount++;

        WriteFile("d.txt");
        _monitor.DeliverChange(ResourceKey.Create("d.txt"), ResourceItemState.File);

        RowPaths(model).Should().Equal("", "c.txt", "d.txt");
        Row(model, "c.txt").Should().BeSameAs(unchangedRow);
        rowsChangedCount.Should().Be(1);
    }

    [Test]
    public async Task ChangeInACheckedFolder_ChecksItAgain_WithoutListingIt()
    {
        CreateFolder("b");
        var model = CreateModel();
        await model.RefreshAsync();
        Row(model, "b").HasChildren.Should().BeFalse();

        WriteFile("b/y.txt");
        _monitor.DeliverChange(ResourceKey.Create("b/y.txt"), ResourceItemState.File);

        Row(model, "b").HasChildren.Should().BeTrue();
        model.ListedFolders.Should().BeEquivalentTo(Keys(""));
    }

    [Test]
    public async Task ChangeOutsideTheFollowedFolders_DoesNotReachTheModel()
    {
        CreateFolder("a/b");
        var model = CreateModel();
        await model.RefreshAsync();
        var rowsChangedCount = 0;
        model.RowsChanged += () => rowsChangedCount++;

        WriteFile("a/b/deep.txt");
        _monitor.DeliverChange(ResourceKey.Create("a/b/deep.txt"), ResourceItemState.File);

        rowsChangedCount.Should().Be(0);
    }

    [Test]
    public async Task ChangeReportedInAnotherLetterCase_StillRelistsTheFolder()
    {
        CreateFolder("Docs");
        var model = CreateModel();
        await model.SetExpandedAsync(ResourceKey.Create("Docs"), true);

        WriteFile("Docs/notes.txt");
        _monitor.DeliverChange(ResourceKey.Create("docs/notes.txt"), ResourceItemState.Absent);

        RowPaths(model).Should().Equal("", "Docs", "Docs/notes.txt");
    }

    [Test]
    public async Task Resync_ListsAndChecksEveryFollowedFolderAgain()
    {
        CreateFolder("b");
        var model = CreateModel();
        await model.RefreshAsync();
        Row(model, "b").HasChildren.Should().BeFalse();

        WriteFile("b/y.txt");
        _monitor.DeliverResync(ResourceKey.DefaultRoot);

        Row(model, "b").HasChildren.Should().BeTrue();
    }

    [Test]
    public async Task ExpandedFolderDeleted_LeavesTheSavedSet()
    {
        CreateFolder("a");
        WriteFile("a/x.txt");
        var model = CreateModel();
        await model.SetExpandedAsync(ResourceKey.Create("a"), true);

        DeletePath("a");
        _monitor.DeliverChange(ResourceKey.Create("a"), ResourceItemState.Absent);

        _folderStateService.ExpandedFolders.Should().BeEmpty();
        RowPaths(model).Should().Equal("");
    }

    [Test]
    public async Task HiddenResources_ShowDimmedOnlyWithShowHiddenFiles()
    {
        UsePolicy(explorerExcludePatterns: [".secret", "build/"], searchExcludePatterns: []);
        CreateFolder("build");
        WriteFile(".secret");
        WriteFile("notes.md");
        WriteFile("notes.md.cel");
        var model = CreateModel();

        await model.RefreshAsync();

        RowPaths(model).Should().Equal("", "notes.md");

        await model.SetShowHiddenFilesAsync(true);

        RowPaths(model).Should().Equal("", "build", ".secret", "notes.md", "notes.md.cel");
        Row(model, "build").IsHidden.Should().BeTrue();
        Row(model, ".secret").IsHidden.Should().BeTrue();
        Row(model, "notes.md.cel").IsHidden.Should().BeTrue();
        Row(model, "notes.md").IsHidden.Should().BeFalse();
    }

    [Test]
    public async Task SearchExcludedFolder_ShowsWithoutShowHiddenFiles()
    {
        UsePolicy(explorerExcludePatterns: [], searchExcludePatterns: ["dist/"]);
        CreateFolder("dist");
        WriteFile("dist/app.js");
        var model = CreateModel();

        await model.RefreshAsync();

        RowPaths(model).Should().Equal("", "dist");
        Row(model, "dist").IsHidden.Should().BeFalse();
        Row(model, "dist").HasChildren.Should().BeTrue();
    }

    [Test]
    public async Task FolderHoldingOnlyHiddenResources_HasAnExpanderOnlyWithShowHiddenFiles()
    {
        UsePolicy(explorerExcludePatterns: ["*.log"], searchExcludePatterns: []);
        CreateFolder("logs");
        WriteFile("logs/app.log");
        var model = CreateModel();

        await model.RefreshAsync();

        Row(model, "logs").HasChildren.Should().BeFalse();

        await model.SetShowHiddenFilesAsync(true);

        Row(model, "logs").HasChildren.Should().BeTrue();
    }

    [Test]
    public async Task Reveal_ExpandsTheFoldersAbove_AndShowsAHiddenResource()
    {
        UsePolicy(explorerExcludePatterns: ["build/"], searchExcludePatterns: []);
        CreateFolder("build/out");
        WriteFile("build/out/app.txt");
        var model = CreateModel();
        await model.RefreshAsync();

        var revealResult = await model.RevealAsync(ResourceKey.Create("build/out/app.txt"));

        revealResult.IsSuccess.Should().BeTrue();
        _workspaceSettings.ShowHiddenFiles.Should().BeTrue();
        _folderStateService.ExpandedFolders
            .Select(folder => new ResourceKey(folder))
            .Should().Equal(Keys("build", "build/out"));
        RowPaths(model).Should().Equal("", "build", "build/out", "build/out/app.txt");
    }

    [Test]
    public async Task Reveal_ThatTurnsShowHiddenFilesOn_ChecksTheCollapsedFoldersAgain()
    {
        UsePolicy(explorerExcludePatterns: ["*.log", "build/"], searchExcludePatterns: []);
        CreateFolder("logs");
        WriteFile("logs/app.log");
        CreateFolder("build");
        WriteFile("build/app.txt");
        var model = CreateModel();
        await model.RefreshAsync();
        Row(model, "logs").HasChildren.Should().BeFalse();

        var revealResult = await model.RevealAsync(ResourceKey.Create("build/app.txt"));

        revealResult.IsSuccess.Should().BeTrue();
        _workspaceSettings.ShowHiddenFiles.Should().BeTrue();
        Row(model, "logs").HasChildren.Should().BeTrue();
    }

    [Test]
    public async Task Reveal_FailsForAMissingResource()
    {
        CreateFolder("a");
        var model = CreateModel();
        await model.RefreshAsync();

        var revealResult = await model.RevealAsync(ResourceKey.Create("a/missing.txt"));

        revealResult.IsFailure.Should().BeTrue();
        _folderStateService.ExpandedFolders.Should().BeEmpty();
    }

    [Test]
    public async Task Reveal_FindsAResourceMadeBeforeItsChangeArrives()
    {
        CreateFolder("a");
        var model = CreateModel();
        await model.RefreshAsync();

        WriteFile("a/new.txt");
        var revealResult = await model.RevealAsync(ResourceKey.Create("a/new.txt"));

        revealResult.IsSuccess.Should().BeTrue();
        RowPaths(model).Should().Equal("", "a", "a/new.txt");
    }

    [Test]
    public async Task Subscription_MatchesTheFollowedFoldersAndTheirChildren()
    {
        CreateFolder("a/b");
        var model = CreateModel();

        await model.RefreshAsync();

        var matcher = _monitor.MatcherSubscriptions.Single().Matcher;
        matcher.IsMatch("a", isFolder: true).Should().BeTrue();
        matcher.IsMatch("a/b", isFolder: true).Should().BeTrue();
        matcher.IsMatch("A/B", isFolder: true).Should().BeTrue();
        matcher.IsMatch("a/b/deep.txt", isFolder: false).Should().BeFalse();
        matcher.CanMatchInside("a").Should().BeTrue();
        matcher.CanMatchInside("a/b").Should().BeFalse();
    }

    [Test]
    public async Task Subscription_IsReplacedWhenTheFollowedFoldersChange()
    {
        CreateFolder("a/b");
        var model = CreateModel();
        await model.RefreshAsync();
        var firstSubscription = _monitor.MatcherSubscriptions.Single();

        await model.SetExpandedAsync(ResourceKey.Create("a"), true);

        _monitor.MatcherSubscriptions.Should().ContainSingle()
            .Which.Should().NotBeSameAs(firstSubscription);
    }

    [Test]
    public async Task Stop_DisposesTheSubscription_AndDropsTheListingsAndChecks()
    {
        CreateFolder("a");
        var model = CreateModel();
        await model.RefreshAsync();

        model.Stop();

        _monitor.MatcherSubscriptions.Should().BeEmpty();
        model.ListedFolders.Should().BeEmpty();
        model.CheckedFolders.Should().BeEmpty();
    }

    [Test]
    public async Task Rows_ReadTheWritableStateAndIconFromTheListing()
    {
        WriteFile("notes.txt");
        var model = CreateModel();

        await model.RefreshAsync();

        var row = Row(model, "notes.txt");
        row.WritableState.Should().Be(WritableState.Writable);
        row.FileIcon.Should().NotBeNull();
    }

    [Test]
    public async Task Rows_ShowAReadOnlyFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("OS read-only attribute behaviour is exercised on Windows.");
        }

        var readOnlyPath = Path.Combine(_projectFolderPath, "frozen.txt");
        File.WriteAllText(readOnlyPath, "x");
        File.SetAttributes(readOnlyPath, File.GetAttributes(readOnlyPath) | FileAttributes.ReadOnly);
        try
        {
            var model = CreateModel();

            await model.RefreshAsync();

            Row(model, "frozen.txt").WritableState.Should().Be(WritableState.ReadOnlyAttribute);
        }
        finally
        {
            // Clear the bit so TearDown can delete the temp folder cleanly.
            File.SetAttributes(readOnlyPath, File.GetAttributes(readOnlyPath) & ~FileAttributes.ReadOnly);
        }
    }

    [Test]
    public async Task Rows_ShowAFileWithTheWindowsHiddenAttribute()
    {
        // The project's patterns decide what the tree hides. The OS hidden attribute does not.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows hidden attribute is only settable on Windows.");
        }

        var hiddenFilePath = Path.Combine(_projectFolderPath, "notes.txt");
        File.WriteAllText(hiddenFilePath, "x");
        File.SetAttributes(hiddenFilePath, File.GetAttributes(hiddenFilePath) | FileAttributes.Hidden);
        var model = CreateModel();

        await model.RefreshAsync();

        RowPaths(model).Should().Contain("notes.txt");
    }

    [Test]
    public async Task Rows_LeaveOutLinks()
    {
        CreateFolder("target");
        WriteFile("target.txt");
        Directory.CreateSymbolicLink(Path.Combine(_projectFolderPath, "linked"), Path.Combine(_projectFolderPath, "target"));
        File.CreateSymbolicLink(Path.Combine(_projectFolderPath, "linked.txt"), Path.Combine(_projectFolderPath, "target.txt"));
        var model = CreateModel();

        await model.RefreshAsync();

        RowPaths(model).Should().Equal("", "target", "target.txt");
    }

    private ResourceTreeModel CreateModel()
    {
        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];
            return new LocalizedString(name, name);
        });
        stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(callInfo =>
        {
            var name = (string)callInfo[0];
            return new LocalizedString(name, name);
        });

        var project = Substitute.For<IProject>();
        project.ProjectFolderPath.Returns(_projectFolderPath);
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        return new ResourceTreeModel(
            Substitute.For<ILogger<ResourceTreeModel>>(),
            _workspaceWrapper,
            projectService,
            Substitute.For<IPlatformInfo>(),
            new IconService(),
            stringLocalizer,
            Substitute.For<IAutomationService>());
    }

    // Builds the policy into a local before configuring the substitute, since building the policy creates other
    // substitutes.
    private void UsePolicy(string[] explorerExcludePatterns, string[] searchExcludePatterns)
    {
        var config = new ProjectConfig
        {
            Explorer = new ExplorerSection { Exclude = explorerExcludePatterns },
            Search = new SearchSection { Exclude = searchExcludePatterns },
        };
        var project = Substitute.For<IProject>();
        project.Config.Returns(config);
        project.ProjectFolderPath.Returns(_projectFolderPath);
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        var policy = new ResourcePolicy(projectService);
        _resourceService.Policy.Returns(policy);
    }

    // Each change on disk is recorded with the project root's resolver. The resource monitor does the same in the
    // application.
    private void WriteFile(string path)
    {
        var filePath = Path.Combine(_projectFolderPath, path);
        File.WriteAllText(filePath, "x");
        RecordNameChanged(filePath);
    }

    private void CreateFolder(string path)
    {
        var folderPath = Path.Combine(_projectFolderPath, path);
        Directory.CreateDirectory(folderPath);
        RecordNameChanged(folderPath);
    }

    private void DeletePath(string path)
    {
        var fullPath = Path.Combine(_projectFolderPath, path);
        Directory.Delete(fullPath, recursive: true);
        RecordNameChanged(fullPath);
    }

    private void RecordNameChanged(string absolutePath)
    {
        var projectRootHandler = _rootHandlers.RootHandlers[ResourceKey.DefaultRoot];
        projectRootHandler.RecordNameChanged(absolutePath);
    }

    private static List<string> RowPaths(ResourceTreeModel model)
    {
        return model.Rows
            .Select(row => row.Resource.Path)
            .ToList();
    }

    private static ResourceViewItem Row(ResourceTreeModel model, string path)
    {
        return model.Rows.Single(row => row.Resource.Path == path);
    }

    private static List<ResourceKey> Keys(params string[] paths)
    {
        return paths
            .Select(path => new ResourceKey(path))
            .ToList();
    }
}
