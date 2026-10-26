using Celbridge.Resources;
using Celbridge.UserInterface;
using Celbridge.UserInterface.ViewModels;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// The resource picker lists either the project's files or its folders, from a walk of the project. A project's
/// downloads folder is chosen from the folder list, so these tests pin that it holds every folder and nothing else.
/// </summary>
[TestFixture]
public class ResourcePickerDialogViewModelTests
{
    private static readonly IconDefinition FileIcon = new("f", "#FFFFFF", "Segoe Fluent Icons", "16");
    private static readonly IconDefinition FolderIcon = new("d", "#FFCC40", "Segoe Fluent Icons", "16");

    private IResourceWalker _walker = null!;
    private ResourcePickerDialogViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        // readme.md
        // assets/
        // docs/notes.md
        // docs/images/logo.png
        _walker = Substitute.For<IResourceWalker>();
        UseWalkedItems(
            Folder("assets"),
            Folder("docs"),
            Folder("docs/images"),
            File("docs/images/logo.png"),
            File("docs/notes.md"),
            File("readme.md"));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Walker.Returns(_walker);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var iconService = Substitute.For<IIconService>();
        iconService.DefaultFolderIcon.Returns(FolderIcon);
        iconService.GetFileIconForFileName(Arg.Any<string>()).Returns(Result<IconDefinition>.Ok(FileIcon));

        // A formatted string comes back as its name and its first argument, so a test can read the count.
        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));
        stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(callInfo =>
        {
            var name = callInfo.Arg<string>();
            var arguments = callInfo.Arg<object[]>();
            return new LocalizedString(name, $"{name}:{arguments[0]}");
        });

        _viewModel = new ResourcePickerDialogViewModel(
            Substitute.For<ILogger<ResourcePickerDialogViewModel>>(),
            workspaceWrapper,
            iconService,
            stringLocalizer);
    }

    [Test]
    public async Task TheFolderList_HoldsEveryFolderAtAnyDepth_AndNoFile()
    {
        _viewModel.InitializeForFolders();
        await _viewModel.LoadItemsAsync();

        _viewModel.FilteredItems.Select(item => item.DisplayText)
            .Should().Equal("assets", "docs", "docs/images");
        _viewModel.FilteredItems.Should().OnlyContain(item => item.IconDefinition == FolderIcon);
        _viewModel.SearchPlaceholder.Should().Be("ResourcePickerDialog_FolderSearchPlaceholder");
    }

    [Test]
    public async Task TheFolderList_WalksWithSearchExcludeSwitchedOff()
    {
        _viewModel.InitializeForFolders();
        await _viewModel.LoadItemsAsync();

        await _walker.Received(1).WalkAsync(ResourceKey.Empty, false, Arg.Any<IPathMatcher?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TheFolderList_IsNarrowedByTheSearch()
    {
        _viewModel.InitializeForFolders();
        await _viewModel.LoadItemsAsync();

        _viewModel.SearchText = "IMA";

        _viewModel.FilteredItems.Should().ContainSingle()
            .Which.ResourceKey.Should().Be(new ResourceKey("docs/images"));
    }

    [Test]
    public async Task TheFileList_HoldsTheFilesWithTheGivenExtensions()
    {
        _viewModel.Initialize(new[] { ".md" }, showPreview: false);
        await _viewModel.LoadItemsAsync();

        _viewModel.FilteredItems.Select(item => item.DisplayText)
            .Should().Equal("docs/notes.md", "readme.md");
        _viewModel.FilteredItems.Should().OnlyContain(item => item.IconDefinition == FileIcon);
        _viewModel.SearchPlaceholder.Should().Be("ResourcePickerDialog_SearchPlaceholder");
    }

    [Test]
    public async Task TheFileList_WalksWithSearchExcludeSwitchedOn()
    {
        _viewModel.Initialize(Array.Empty<string>(), showPreview: false);
        await _viewModel.LoadItemsAsync();

        await _walker.Received(1).WalkAsync(ResourceKey.Empty, true, Arg.Any<IPathMatcher?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AReadOnlyFile_IsListedAsReadOnly()
    {
        var readOnlyFile = new WalkedItem(
            new ResourceKey("locked.md"),
            "/project/locked.md",
            false,
            1,
            default,
            FileSystemAttributes.ReadOnly);
        UseWalkedItems(readOnlyFile, File("open.md"));

        _viewModel.Initialize(Array.Empty<string>(), showPreview: false);
        await _viewModel.LoadItemsAsync();

        _viewModel.FilteredItems.Single(item => item.DisplayText == "locked.md")
            .WritableState.Should().Be(WritableState.ReadOnlyAttribute);
        _viewModel.FilteredItems.Single(item => item.DisplayText == "open.md")
            .WritableState.Should().Be(WritableState.Writable);
    }

    [Test]
    public async Task TheList_ShowsThatItIsLoading_UntilTheWalkFinishes()
    {
        var walkCompletion = new TaskCompletionSource<Result<IReadOnlyList<WalkedItem>>>();
        _walker.WalkAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<IPathMatcher?>(), Arg.Any<CancellationToken>())
            .Returns(walkCompletion.Task);

        _viewModel.Initialize(Array.Empty<string>(), showPreview: false);
        var loadTask = _viewModel.LoadItemsAsync();

        _viewModel.IsLoading.Should().BeTrue();
        _viewModel.LoadingVisibility.Should().Be(Visibility.Visible);
        _viewModel.FilteredItems.Should().BeEmpty();

        IReadOnlyList<WalkedItem> walkedItems = new[] { File("readme.md") };
        walkCompletion.SetResult(Result<IReadOnlyList<WalkedItem>>.Ok(walkedItems));
        await loadTask;

        _viewModel.IsLoading.Should().BeFalse();
        _viewModel.LoadingVisibility.Should().Be(Visibility.Collapsed);
        _viewModel.FilteredItems.Select(item => item.DisplayText).Should().Equal("readme.md");
    }

    [Test]
    public async Task TheList_ShowsTheFirstMatches_AndCountsTheRest()
    {
        var files = Enumerable.Range(0, ResourcePickerDialogViewModel.MaxShownMatches + 50)
            .Select(index => File($"file_{index:D3}.txt"))
            .ToArray();
        UseWalkedItems(files);

        _viewModel.Initialize(Array.Empty<string>(), showPreview: false);
        await _viewModel.LoadItemsAsync();

        _viewModel.FilteredItems.Should().HaveCount(ResourcePickerDialogViewModel.MaxShownMatches);
        _viewModel.MoreMatchesText.Should().Be("ResourcePickerDialog_MoreMatches_Many:50");
        _viewModel.MoreMatchesVisibility.Should().Be(Visibility.Visible);

        // An item beyond the shown list is still one of the matches.
        _viewModel.FindMatchingItem(new ResourceKey("file_249.txt")).Should().NotBeNull();

        _viewModel.SearchText = "file_24";

        _viewModel.FilteredItems.Should().HaveCount(10);
        _viewModel.MoreMatchesVisibility.Should().Be(Visibility.Collapsed);
    }

    [Test]
    public async Task TheList_StopsLoadingAndStaysEmpty_WhenTheWalkThrows()
    {
        _walker.WalkAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<IPathMatcher?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result<IReadOnlyList<WalkedItem>>>(new IOException("The disk went away")));

        _viewModel.Initialize(Array.Empty<string>(), showPreview: false);
        await _viewModel.LoadItemsAsync();

        _viewModel.IsLoading.Should().BeFalse();
        _viewModel.FilteredItems.Should().BeEmpty();
    }

    private void UseWalkedItems(params WalkedItem[] items)
    {
        IReadOnlyList<WalkedItem> walkedItems = items;
        _walker.WalkAsync(Arg.Any<ResourceKey>(), Arg.Any<bool>(), Arg.Any<IPathMatcher?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<IReadOnlyList<WalkedItem>>.Ok(walkedItems)));
    }

    private static WalkedItem Folder(string key)
    {
        return new WalkedItem(new ResourceKey(key), "/project/" + key, true, 0, default, FileSystemAttributes.None);
    }

    private static WalkedItem File(string key)
    {
        return new WalkedItem(new ResourceKey(key), "/project/" + key, false, 1, default, FileSystemAttributes.None);
    }
}
