using Celbridge.Dialog;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.UserInterface;
using Celbridge.UserInterface.ViewModels;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// The resource picker browses the project one folder at a time, and searches a walk of the whole project. These
/// tests cover what a folder lists in each mode, and what the picker hides with Show Hidden Files off and on. They
/// also cover the order of the search's matches, where the picker starts, the primary button and the checks on an
/// automated answer.
/// </summary>
[TestFixture]
public class ResourcePickerViewModelTests
{
    private static readonly IconDefinition FileIcon = new("f", "#FFFFFF", "Segoe Fluent Icons", "16");
    private static readonly IconDefinition FolderIcon = new("d", "#FFCC40", "Segoe Fluent Icons", "16");

    // Each folder's children, in the order a folder's listing gives them: folders first, then files.
    private Dictionary<ResourceKey, List<FolderChild>> _folders = null!;
    private IResourceWalker _walker = null!;
    private IBindableWorkspaceSettings _workspaceSettings = null!;
    private ResourcePickerViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        // assets/
        // docs/images/logo.png
        // docs/notes.md
        // docs/notes.md.cel
        // drafts/plan.md          (the project's Explorer list excludes drafts)
        // locked.md               (read-only)
        // readme.md
        _folders = new Dictionary<ResourceKey, List<FolderChild>>();
        AddFolder("", Folder("assets"), Folder("docs"), Folder("drafts"), File("locked.md", FileSystemAttributes.ReadOnly), File("readme.md"));
        AddFolder("assets");
        AddFolder("docs", Folder("docs/images"), File("docs/notes.md"), File("docs/notes.md.cel"));
        AddFolder("docs/images", File("docs/images/logo.png"));
        AddFolder("drafts", File("drafts/plan.md"));

        var fileSystem = Substitute.For<IResourceFileSystem>();
        fileSystem.EnumerateFolderAsync(Arg.Any<ResourceKey>()).Returns(callInfo =>
        {
            var folder = callInfo.Arg<ResourceKey>();
            if (_folders.TryGetValue(folder, out var children))
            {
                IReadOnlyList<FolderChild> listing = children;
                return Task.FromResult(Result<IReadOnlyList<FolderChild>>.Ok(listing));
            }

            return Task.FromResult(Result<IReadOnlyList<FolderChild>>.Fail($"Not a folder: '{folder}'"));
        });
        fileSystem.GetResourceItemAsync(Arg.Any<ResourceKey>()).Returns(callInfo =>
        {
            var resource = callInfo.Arg<ResourceKey>();
            var child = _folders.Values
                .SelectMany(children => children)
                .FirstOrDefault(candidate => candidate.Resource == resource);
            if (child is null)
            {
                return Task.FromResult(Result<ResourceItem>.Fail($"Resource not found: '{resource}'"));
            }

            return Task.FromResult(Result<ResourceItem>.Ok(new ResourceItem(child.Resource, child.IsFolder)));
        });

        // The walk lists the project the way the real walker does: each folder before its contents, and nothing
        // inside a folder the filter rules out.
        _walker = Substitute.For<IResourceWalker>();
        _walker.WalkAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<IPathMatcher?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var filter = callInfo.Arg<IPathMatcher?>();
                IReadOnlyList<WalkedItem> walkedItems = Walk(ResourceKey.Empty, filter);
                return Task.FromResult(Result<IReadOnlyList<WalkedItem>>.Ok(walkedItems));
            });

        var config = new ProjectConfig
        {
            Explorer = new ExplorerSection
            {
                Exclude = ["drafts"]
            }
        };
        var project = Substitute.For<IProject>();
        project.ProjectName.Returns("Acme");
        project.Config.Returns(config);
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        var rootHandlerRegistry = Substitute.For<IRootHandlerRegistry>();
        rootHandlerRegistry.RootHandlers.Returns(new Dictionary<string, IResourceRootHandler>());

        var policy = new ResourcePolicy(projectService);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Walker.Returns(_walker);
        resourceService.FileSystem.Returns(fileSystem);
        resourceService.Policy.Returns(policy);
        resourceService.RootHandlers.Returns(rootHandlerRegistry);

        _workspaceSettings = Substitute.For<IBindableWorkspaceSettings>();

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        workspaceService.BindableWorkspaceSettings.Returns(_workspaceSettings);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var iconService = Substitute.For<IIconService>();
        iconService.DefaultFolderIcon.Returns(FolderIcon);
        iconService.GetFileIconForFileName(Arg.Any<string>()).Returns(Result<IconDefinition>.Ok(FileIcon));

        // A formatted string comes back as the string's name and first argument, so a test can read the argument.
        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));
        stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(callInfo =>
        {
            var name = callInfo.Arg<string>();
            var arguments = callInfo.Arg<object[]>();
            return new LocalizedString(name, $"{name}:{arguments[0]}");
        });

        _viewModel = new ResourcePickerViewModel(
            Substitute.For<ILogger<ResourcePickerViewModel>>(),
            workspaceWrapper,
            projectService,
            iconService,
            stringLocalizer);
    }

    [Test]
    public async Task OpenMode_ListsTheFoldersThenTheFiles_AndLeavesOutWhatTheExplorerHides()
    {
        await OpenAsync(new ResourcePickerOptions());

        ShownNames().Should().Equal("assets", "docs", "locked.md", "readme.md");
        _viewModel.Crumbs.Select(crumb => crumb.Name).Should().Equal("Acme");
        _viewModel.CanGoUp.Should().BeFalse();
        _viewModel.TitleText.Should().Be("ResourcePickerDialog_OpenTitle");
        _viewModel.PrimaryButtonText.Should().Be("DialogButton_Open");

        await _viewModel.OpenFolderAsync(new ResourceKey("docs"));

        ShownNames().Should().Equal("images", "notes.md");
    }

    [Test]
    public async Task OpenMode_ListsOnlyTheFilesWithAnAllowedExtension()
    {
        await OpenAsync(new ResourcePickerOptions
        {
            Extensions = [".PNG"]
        });
        await _viewModel.OpenFolderAsync(new ResourceKey("docs/images"));

        ShownNames().Should().Equal("logo.png");
        _viewModel.ExtensionFilterText.Should().Be("*.png");

        await _viewModel.OpenFolderAsync(new ResourceKey("docs"));

        ShownNames().Should().Equal("images");
    }

    [Test]
    public async Task FolderMode_ListsOnlyFolders_AndChoosesTheCurrentFolder_WhateverIsSelected()
    {
        await OpenAsync(new ResourcePickerOptions
        {
            Mode = ResourcePickerMode.Folder,
            StartFolder = new ResourceKey("docs")
        });

        ShownNames().Should().Equal("images");
        _viewModel.ShownItems.Should().OnlyContain(item => item.IconDefinition == FolderIcon);
        _viewModel.TitleText.Should().Be("ResourcePickerDialog_FolderTitle");
        _viewModel.PrimaryButtonText.Should().Be("DialogButton_SelectFolder");
        _viewModel.SelectedItem.Should().BeNull();
        _viewModel.IsPrimaryButtonEnabled.Should().BeTrue();
        _viewModel.GetChoice().Should().Be(new ResourceKey("docs"));

        _viewModel.SelectedItem = _viewModel.ShownItems.Single();

        _viewModel.GetChoice().Should().Be(new ResourceKey("docs"));
    }

    [Test]
    public async Task FolderMode_InvokingAFolder_OpensTheFolder_AndNeverChoosesIt()
    {
        await OpenAsync(new ResourcePickerOptions
        {
            Mode = ResourcePickerMode.Folder
        });

        var docsItem = _viewModel.ShownItems.Single(item => item.Name == "docs");
        (await _viewModel.InvokeItemAsync(docsItem)).Should().BeNull();

        _viewModel.CurrentFolder.Should().Be(new ResourceKey("docs"));
        _viewModel.GetChoice().Should().Be(new ResourceKey("docs"));

        // The images folder has no subfolders, so the list opens empty.
        var imagesItem = _viewModel.ShownItems.Single();
        (await _viewModel.InvokeItemAsync(imagesItem)).Should().BeNull();

        _viewModel.CurrentFolder.Should().Be(new ResourceKey("docs/images"));
        _viewModel.ShownItems.Should().BeEmpty();
        _viewModel.GetChoice().Should().Be(new ResourceKey("docs/images"));
    }

    [Test]
    public async Task FolderMode_OpensTheStartFolder_AndIgnoresTheInitialSelection()
    {
        await OpenAsync(new ResourcePickerOptions
        {
            Mode = ResourcePickerMode.Folder,
            InitialSelection = new ResourceKey("docs/images"),
            StartFolder = new ResourceKey("assets")
        });

        _viewModel.CurrentFolder.Should().Be(new ResourceKey("assets"));
        _viewModel.SelectedItem.Should().BeNull();
        _viewModel.GetChoice().Should().Be(new ResourceKey("assets"));
    }

    [Test]
    public async Task FolderMode_HasNoChoice_DuringASearch()
    {
        await OpenAsync(new ResourcePickerOptions
        {
            Mode = ResourcePickerMode.Folder,
            StartFolder = new ResourceKey("docs")
        });
        await _viewModel.LoadSearchItemsAsync();

        _viewModel.SearchText = "images";

        _viewModel.IsPrimaryButtonEnabled.Should().BeFalse();
        _viewModel.GetChoice().Should().BeNull();

        _viewModel.SelectedItem = _viewModel.ShownItems.Single();

        _viewModel.IsPrimaryButtonEnabled.Should().BeFalse();
        _viewModel.GetChoice().Should().BeNull();

        _viewModel.ClearSearch();

        _viewModel.IsPrimaryButtonEnabled.Should().BeTrue();
        _viewModel.GetChoice().Should().Be(new ResourceKey("docs"));
    }

    [Test]
    public async Task ShowHiddenFiles_ListsHiddenItems_Dimmed()
    {
        _workspaceSettings.ShowHiddenFiles.Returns(true);

        await OpenAsync(new ResourcePickerOptions());

        ShownNames().Should().Equal("assets", "docs", "drafts", "locked.md", "readme.md");
        _viewModel.ShownItems.Single(item => item.Name == "drafts").IsDimmed.Should().BeTrue();
        _viewModel.ShownItems.Single(item => item.Name == "readme.md").IsDimmed.Should().BeFalse();

        await _viewModel.OpenFolderAsync(new ResourceKey("docs"));

        ShownNames().Should().Equal("images", "notes.md", "notes.md.cel");
        _viewModel.ShownItems.Single(item => item.Name == "notes.md.cel").IsHidden.Should().BeTrue();
    }

    [Test]
    public async Task AReadOnlyFile_IsDimmed_WithTheReadOnlyMessage()
    {
        await OpenAsync(new ResourcePickerOptions());

        var lockedItem = _viewModel.ShownItems.Single(item => item.Name == "locked.md");
        lockedItem.WritableState.Should().Be(WritableState.ReadOnlyAttribute);
        lockedItem.IsDimmed.Should().BeTrue();
        lockedItem.TooltipText.Should().NotBeNullOrEmpty();

        var readmeItem = _viewModel.ShownItems.Single(item => item.Name == "readme.md");
        readmeItem.WritableState.Should().Be(WritableState.Writable);
        readmeItem.TooltipText.Should().BeNull();
    }

    [Test]
    public async Task TheBreadcrumb_AndUp_FollowTheCurrentFolder()
    {
        await OpenAsync(new ResourcePickerOptions());
        await _viewModel.OpenFolderAsync(new ResourceKey("docs/images"));

        _viewModel.Crumbs.Select(crumb => crumb.Name).Should().Equal("Acme", "docs", "images");
        _viewModel.Crumbs.Select(crumb => crumb.Folder)
            .Should().Equal(ResourceKey.Empty, new ResourceKey("docs"), new ResourceKey("docs/images"));
        _viewModel.Crumbs.Select(crumb => crumb.IsProjectFolder).Should().Equal(true, false, false);
        _viewModel.CanGoUp.Should().BeTrue();

        await _viewModel.GoUpAsync();

        _viewModel.CurrentFolder.Should().Be(new ResourceKey("docs"));
        ShownNames().Should().Equal("images", "notes.md");
    }

    [Test]
    public async Task Up_IsOff_WhileTheSearchBoxHasText()
    {
        await OpenAsync(new ResourcePickerOptions
        {
            StartFolder = new ResourceKey("docs")
        });
        await _viewModel.LoadSearchItemsAsync();

        _viewModel.SearchText = "notes";

        _viewModel.CanGoUp.Should().BeFalse();
        await _viewModel.GoUpAsync();
        _viewModel.CurrentFolder.Should().Be(new ResourceKey("docs"));
        _viewModel.SearchText.Should().Be("notes");

        _viewModel.ClearSearch();

        _viewModel.CanGoUp.Should().BeTrue();
    }

    [Test]
    public async Task AFolderThatCannotBeListed_LeavesThePickerWhereItIs()
    {
        await OpenAsync(new ResourcePickerOptions());
        await _viewModel.OpenFolderAsync(new ResourceKey("docs"));

        await _viewModel.OpenFolderAsync(new ResourceKey("missing"));

        _viewModel.CurrentFolder.Should().Be(new ResourceKey("docs"));
        ShownNames().Should().Equal("images", "notes.md");
    }

    [Test]
    public async Task TheInitialSelection_OpensItsFolder_WithTheResourceSelected()
    {
        await OpenAsync(new ResourcePickerOptions
        {
            InitialSelection = new ResourceKey("docs/notes.md"),
            StartFolder = new ResourceKey("assets")
        });

        _viewModel.CurrentFolder.Should().Be(new ResourceKey("docs"));
        _viewModel.SelectedItem!.Resource.Should().Be(new ResourceKey("docs/notes.md"));
        _viewModel.IsPrimaryButtonEnabled.Should().BeTrue();
    }

    [Test]
    public async Task APickerSetUpAgain_StartsEmpty_UntilItOpens()
    {
        var options = new ResourcePickerOptions
        {
            Mode = ResourcePickerMode.Folder,
            StartFolder = new ResourceKey("docs/images")
        };
        await OpenAsync(options);
        await _viewModel.LoadSearchItemsAsync();
        _viewModel.SearchText = "images";

        _viewModel.Initialize(options);

        _viewModel.SearchText.Should().BeEmpty();
        _viewModel.CurrentFolder.Should().Be(ResourceKey.Empty);
        _viewModel.Crumbs.Should().BeEmpty();
        _viewModel.ShownItems.Should().BeEmpty();
        _viewModel.SelectedItem.Should().BeNull();
        _viewModel.PrimaryButtonText.Should().Be("DialogButton_SelectFolder");
        _viewModel.IsPrimaryButtonEnabled.Should().BeFalse();

        await _viewModel.OpenAsync();

        _viewModel.CurrentFolder.Should().Be(new ResourceKey("docs/images"));
        _viewModel.GetChoice().Should().Be(new ResourceKey("docs/images"));
        _viewModel.IsPrimaryButtonEnabled.Should().BeTrue();
    }

    [Test]
    public async Task AnInitialSelectionInAMissingFolder_GivesWayToTheStartFolder_AndThenTheProjectFolder()
    {
        await OpenAsync(new ResourcePickerOptions
        {
            InitialSelection = new ResourceKey("missing/notes.md"),
            StartFolder = new ResourceKey("docs")
        });

        _viewModel.CurrentFolder.Should().Be(new ResourceKey("docs"));
        _viewModel.SelectedItem.Should().BeNull();

        await OpenAsync(new ResourcePickerOptions
        {
            InitialSelection = new ResourceKey("temp:notes.md"),
            StartFolder = new ResourceKey("missing")
        });

        _viewModel.CurrentFolder.Should().Be(ResourceKey.Empty);
    }

    [Test]
    public async Task TheSearch_MatchesPaths_InTheExplorersOrder()
    {
        await OpenAsync(new ResourcePickerOptions());
        await _viewModel.LoadSearchItemsAsync();

        _viewModel.SearchText = "DOCS/";

        ShownPaths().Should().Equal("docs/images", "docs/images/logo.png", "docs/notes.md");
        _viewModel.IsSearching.Should().BeTrue();
        _viewModel.BreadcrumbVisibility.Should().Be(Visibility.Collapsed);
        _viewModel.SearchLocationVisibility.Should().Be(Visibility.Visible);
        _viewModel.ShownItems[1].FolderText.Should().Be("docs/images");

        _viewModel.SearchText = "plan";

        _viewModel.ShownItems.Should().BeEmpty();

        _viewModel.ClearSearch().Should().BeTrue();

        ShownNames().Should().Equal("assets", "docs", "locked.md", "readme.md");
        _viewModel.BreadcrumbVisibility.Should().Be(Visibility.Visible);
        _viewModel.ClearSearch().Should().BeFalse();
    }

    [Test]
    public async Task TheSearch_ReadsAWildcardPattern_AsTheSearchPanelsFileFilter()
    {
        await OpenAsync(new ResourcePickerOptions());
        await _viewModel.LoadSearchItemsAsync();

        _viewModel.SearchText = "*.md";
        ShownPaths().Should().Equal("docs/notes.md", "locked.md", "readme.md");

        _viewModel.SearchText = "n*.md";
        ShownPaths().Should().Equal("docs/notes.md");

        _viewModel.SearchText = "?ocked.MD";
        ShownPaths().Should().Equal("locked.md");

        _viewModel.SearchText = "*.{md,png}";
        ShownPaths().Should().Equal("docs/images/logo.png", "docs/notes.md", "locked.md", "readme.md");

        _viewModel.SearchText = "*.m";
        ShownPaths().Should().BeEmpty();
    }

    [Test]
    public async Task TheSearch_WithAWildcardPattern_FollowsTheFileFiltersRulesForFoldersAndSlashes()
    {
        await OpenAsync(new ResourcePickerOptions());
        await _viewModel.LoadSearchItemsAsync();

        // A matched folder takes everything inside it.
        _viewModel.SearchText = "d*";
        ShownPaths().Should().Equal("docs", "docs/images", "docs/images/logo.png", "docs/notes.md");

        // A slash before the end starts the pattern at the project folder.
        _viewModel.SearchText = "images/*";
        ShownPaths().Should().BeEmpty();

        _viewModel.SearchText = "**/images/*";
        ShownPaths().Should().Equal("docs/images/logo.png");

        _viewModel.SearchText = "docs/*.md";
        ShownPaths().Should().Equal("docs/notes.md");
    }

    [Test]
    public async Task TheSearch_ReadsBracketsAndBraces_AsPlainText_WithoutAStarOrQuestionMark()
    {
        _folders[ResourceKey.Empty].Add(File("report [final].md"));
        _folders[ResourceKey.Empty].Add(File("notes {draft}.md"));
        await OpenAsync(new ResourcePickerOptions());
        await _viewModel.LoadSearchItemsAsync();

        _viewModel.SearchText = "report [final]";
        ShownPaths().Should().Equal("report [final].md");

        _viewModel.SearchText = "{draft}";
        ShownPaths().Should().Equal("notes {draft}.md");

        // A question mark makes the text a pattern, where brackets form a class.
        _viewModel.SearchText = "?ocked.[m]d";
        ShownPaths().Should().Equal("locked.md");
    }

    [Test]
    public async Task TheSearch_FindsNothing_WhileAWildcardPatternIsIncomplete()
    {
        await OpenAsync(new ResourcePickerOptions());
        await _viewModel.LoadSearchItemsAsync();

        _viewModel.SearchText = "*.{md";

        _viewModel.ShownItems.Should().BeEmpty();
        _viewModel.IsLoading.Should().BeFalse();
    }

    [Test]
    public async Task TheSearch_InFolderMode_FindsOnlyFolders()
    {
        await OpenAsync(new ResourcePickerOptions
        {
            Mode = ResourcePickerMode.Folder
        });
        await _viewModel.LoadSearchItemsAsync();

        _viewModel.SearchText = "docs";

        ShownPaths().Should().Equal("docs", "docs/images");
    }

    [Test]
    public async Task TheWalk_SkipsTheContentsOfAHiddenFolder()
    {
        await OpenAsync(new ResourcePickerOptions());
        await _viewModel.LoadSearchItemsAsync();

        await _walker.Received(1).WalkAsync(
            ResourceKey.Empty,
            false,
            Arg.Is<IPathMatcher?>(filter => filter != null && !filter.CanMatchInside("drafts")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ShowHiddenFiles_LetsTheSearchFindHiddenItems_Dimmed()
    {
        _workspaceSettings.ShowHiddenFiles.Returns(true);

        await OpenAsync(new ResourcePickerOptions());
        await _viewModel.LoadSearchItemsAsync();
        _viewModel.SearchText = "plan";

        ShownPaths().Should().Equal("drafts/plan.md");
        _viewModel.ShownItems[0].IsDimmed.Should().BeTrue();
    }

    [Test]
    public async Task TheSearch_ShowsThatItIsLoading_UntilTheWalkFinishes()
    {
        var walkCompletion = new TaskCompletionSource<Result<IReadOnlyList<WalkedItem>>>();
        _walker.WalkAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<IPathMatcher?>(), Arg.Any<CancellationToken>())
            .Returns(walkCompletion.Task);

        await OpenAsync(new ResourcePickerOptions());
        _viewModel.SearchText = "readme";

        _viewModel.IsLoading.Should().BeTrue();
        _viewModel.LoadingVisibility.Should().Be(Visibility.Visible);
        _viewModel.ShownItems.Should().BeEmpty();

        IReadOnlyList<WalkedItem> walkedItems = Walk(ResourceKey.Empty, null);
        walkCompletion.SetResult(Result<IReadOnlyList<WalkedItem>>.Ok(walkedItems));
        await _viewModel.LoadSearchItemsAsync();

        _viewModel.IsLoading.Should().BeFalse();
        ShownPaths().Should().Equal("readme.md");
    }

    [Test]
    public async Task TheSearch_FindsNothing_WhenTheWalkThrows()
    {
        _walker.WalkAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<IPathMatcher?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result<IReadOnlyList<WalkedItem>>>(new IOException("The disk went away")));

        await OpenAsync(new ResourcePickerOptions());
        await _viewModel.LoadSearchItemsAsync();
        _viewModel.SearchText = "readme";

        _viewModel.IsLoading.Should().BeFalse();
        _viewModel.ShownItems.Should().BeEmpty();
    }

    [Test]
    public async Task TheList_ShowsTheFirstItems_AndCountsTheRest()
    {
        var files = Enumerable.Range(0, ResourcePickerViewModel.MaxShownFolderItems + 50)
            .Select(index => File($"many/file_{index:D3}.txt"))
            .ToArray();
        _folders[ResourceKey.Empty].Insert(0, Folder("many"));
        AddFolder("many", files);

        await OpenAsync(new ResourcePickerOptions());
        await _viewModel.OpenFolderAsync(new ResourceKey("many"));

        _viewModel.ShownItems.Should().HaveCount(ResourcePickerViewModel.MaxShownFolderItems);
        _viewModel.MoreItemsText.Should().Be("ResourcePickerDialog_MoreItems_Many:50");
        _viewModel.MoreItemsVisibility.Should().Be(Visibility.Visible);

        await _viewModel.LoadSearchItemsAsync();
        _viewModel.SearchText = "file_";

        var otherMatchCount = files.Length - ResourcePickerViewModel.MaxShownMatches;
        _viewModel.ShownItems.Should().HaveCount(ResourcePickerViewModel.MaxShownMatches);
        _viewModel.MoreItemsText.Should().Be($"ResourcePickerDialog_MoreMatches_Many:{otherMatchCount}");

        _viewModel.SearchText = "file_24";

        _viewModel.ShownItems.Should().HaveCount(10);
        _viewModel.MoreItemsVisibility.Should().Be(Visibility.Collapsed);
    }

    [Test]
    public async Task OpeningAFolderResult_ClearsTheSearch_AndOpeningAFile_ChoosesIt()
    {
        await OpenAsync(new ResourcePickerOptions());
        await _viewModel.LoadSearchItemsAsync();
        _viewModel.SearchText = "images";

        var folderResult = _viewModel.ShownItems.Single(item => item.IsFolder);
        (await _viewModel.InvokeItemAsync(folderResult)).Should().BeNull();

        _viewModel.SearchText.Should().BeEmpty();
        _viewModel.CurrentFolder.Should().Be(new ResourceKey("docs/images"));

        var fileItem = _viewModel.ShownItems.Single();
        (await _viewModel.InvokeItemAsync(fileItem)).Should().Be(new ResourceKey("docs/images/logo.png"));
    }

    [Test]
    public async Task OpenMode_ThePrimaryButton_ChoosesOnlyASelectedFile()
    {
        await OpenAsync(new ResourcePickerOptions());

        _viewModel.IsPrimaryButtonEnabled.Should().BeFalse();

        _viewModel.SelectedItem = _viewModel.ShownItems.Single(item => item.Name == "docs");
        _viewModel.IsPrimaryButtonEnabled.Should().BeFalse();
        _viewModel.GetChoice().Should().BeNull();
        _viewModel.CurrentFolder.Should().Be(ResourceKey.Empty);

        _viewModel.SelectedItem = _viewModel.ShownItems.Single(item => item.Name == "readme.md");
        _viewModel.IsPrimaryButtonEnabled.Should().BeTrue();
        _viewModel.GetChoice().Should().Be(new ResourceKey("readme.md"));
    }

    [Test]
    public async Task AnAnswer_InOpenMode_MustNameAFileThePickerLists()
    {
        _viewModel.Initialize(new ResourcePickerOptions
        {
            Extensions = [".md"]
        });

        (await _viewModel.CheckAnswerAsync(new ResourceKey("docs/notes.md"))).Value
            .Should().Be(new ResourceKey("docs/notes.md"));
        (await _viewModel.CheckAnswerAsync(new ResourceKey("docs"))).IsFailure.Should().BeTrue();
        (await _viewModel.CheckAnswerAsync(new ResourceKey("docs/images/logo.png"))).IsFailure.Should().BeTrue();
        (await _viewModel.CheckAnswerAsync(new ResourceKey("docs/missing.md"))).IsFailure.Should().BeTrue();
        (await _viewModel.CheckAnswerAsync(new ResourceKey("drafts/plan.md"))).IsFailure.Should().BeTrue();
        (await _viewModel.CheckAnswerAsync(new ResourceKey("temp:notes.md"))).IsFailure.Should().BeTrue();
        (await _viewModel.CheckAnswerAsync(ResourceKey.Empty)).IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task AnAnswer_InFolderMode_MustNameAFolder_ThePickerLists()
    {
        _viewModel.Initialize(new ResourcePickerOptions
        {
            Mode = ResourcePickerMode.Folder
        });

        (await _viewModel.CheckAnswerAsync(new ResourceKey("docs/images"))).Value
            .Should().Be(new ResourceKey("docs/images"));
        (await _viewModel.CheckAnswerAsync(ResourceKey.Empty)).Value.Should().Be(ResourceKey.Empty);
        (await _viewModel.CheckAnswerAsync(new ResourceKey("readme.md"))).IsFailure.Should().BeTrue();
        (await _viewModel.CheckAnswerAsync(new ResourceKey("drafts"))).IsFailure.Should().BeTrue();
    }

    private async Task OpenAsync(ResourcePickerOptions options)
    {
        _viewModel.Initialize(options);
        await _viewModel.OpenAsync();
    }

    private List<string> ShownNames()
    {
        return _viewModel.ShownItems
            .Select(item => item.Name)
            .ToList();
    }

    private List<string> ShownPaths()
    {
        return _viewModel.ShownItems
            .Select(item => item.Resource.Path)
            .ToList();
    }

    private void AddFolder(string folder, params FolderChild[] children)
    {
        _folders[new ResourceKey(folder)] = children.ToList();
    }

    private List<WalkedItem> Walk(ResourceKey folder, IPathMatcher? filter)
    {
        var walkedItems = new List<WalkedItem>();
        foreach (var child in _folders[folder])
        {
            var walkedItem = new WalkedItem(
                child.Resource,
                "/project/" + child.Resource.Path,
                child.IsFolder,
                child.Size,
                child.ModifiedUtc,
                child.Attributes);
            walkedItems.Add(walkedItem);

            if (child.IsFolder
                && (filter is null || filter.CanMatchInside(child.Resource.Path)))
            {
                walkedItems.AddRange(Walk(child.Resource, filter));
            }
        }

        return walkedItems;
    }

    private static FolderChild Folder(string key)
    {
        return new FolderChild(new ResourceKey(key), true, 0, default, FileSystemAttributes.None);
    }

    private static FolderChild File(string key, FileSystemAttributes attributes = FileSystemAttributes.None)
    {
        return new FolderChild(new ResourceKey(key), false, 1, default, attributes);
    }
}
