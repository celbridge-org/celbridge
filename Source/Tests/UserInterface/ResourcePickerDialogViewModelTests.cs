using Celbridge.Resources;
using Celbridge.UserInterface;
using Celbridge.UserInterface.ViewModels;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// The resource picker lists either the project's files or its folders, over a project tree built for each
/// test. A project's downloads folder is chosen from the folder list, so these tests pin that it holds every
/// folder and nothing else.
/// </summary>
[TestFixture]
public class ResourcePickerDialogViewModelTests
{
    private static readonly IconDefinition FileIcon = new("f", "#FFFFFF", "Segoe Fluent Icons", "16");
    private static readonly IconDefinition FolderIcon = new("d", "#FFCC40", "Segoe Fluent Icons", "16");

    private IResourceRegistry _registry = null!;
    private Dictionary<IResource, ResourceKey> _resourceKeys = null!;
    private ResourcePickerDialogViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _resourceKeys = new Dictionary<IResource, ResourceKey>();

        // readme.md
        // assets/
        // docs/notes.md
        // docs/images/logo.png
        var projectFolder = CreateFolder(string.Empty);
        AddFile(projectFolder, "readme.md");
        CreateFolder("assets", projectFolder);
        var docsFolder = CreateFolder("docs", projectFolder);
        AddFile(docsFolder, "notes.md");
        var imagesFolder = CreateFolder("docs/images", docsFolder);
        AddFile(imagesFolder, "logo.png");

        _registry = Substitute.For<IResourceRegistry>();
        _registry.ProjectFolder.Returns(projectFolder);
        _registry.GetResourceKey(Arg.Any<IResource>())
            .Returns(callInfo => _resourceKeys[callInfo.Arg<IResource>()]);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_registry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var iconService = Substitute.For<IIconService>();
        iconService.DefaultFolderIcon.Returns(FolderIcon);

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));

        _viewModel = new ResourcePickerDialogViewModel(workspaceWrapper, iconService, stringLocalizer);
    }

    [Test]
    public void TheFolderList_HoldsEveryFolderAtAnyDepth_AndNoFile()
    {
        _viewModel.InitializeForFolders();

        _viewModel.FilteredItems.Select(item => item.DisplayText)
            .Should().Equal("assets", "docs", "docs/images");
        _viewModel.FilteredItems.Should().OnlyContain(item => item.IconDefinition == FolderIcon);
        _viewModel.SearchPlaceholder.Should().Be("ResourcePickerDialog_FolderSearchPlaceholder");
    }

    [Test]
    public void TheFolderList_IsNarrowedByTheSearch()
    {
        _viewModel.InitializeForFolders();

        _viewModel.SearchText = "IMA";

        _viewModel.FilteredItems.Should().ContainSingle()
            .Which.ResourceKey.Should().Be(new ResourceKey("docs/images"));
    }

    [Test]
    public void TheFileList_HoldsTheFilesWithTheGivenExtensions()
    {
        _viewModel.Initialize(new[] { ".md" }, showPreview: false);

        _viewModel.FilteredItems.Select(item => item.DisplayText)
            .Should().Equal("docs/notes.md", "readme.md");
        _viewModel.FilteredItems.Should().OnlyContain(item => item.IconDefinition == FileIcon);
        _viewModel.SearchPlaceholder.Should().Be("ResourcePickerDialog_SearchPlaceholder");
    }

    private IFolderResource CreateFolder(string path, IFolderResource? parentFolder = null)
    {
        var folder = Substitute.For<IFolderResource>();
        folder.Name.Returns(Path.GetFileName(path));
        folder.Children.Returns(new List<IResource>());

        _resourceKeys[folder] = new ResourceKey(path);
        parentFolder?.Children.Add(folder);

        return folder;
    }

    private void AddFile(IFolderResource parentFolder, string name)
    {
        var parentFolderKey = _resourceKeys[parentFolder];
        var path = name;
        if (!parentFolderKey.IsEmpty)
        {
            path = $"{parentFolderKey.Path}/{name}";
        }

        var file = Substitute.For<IFileResource>();
        file.Name.Returns(name);
        file.Icon.Returns(FileIcon);

        _resourceKeys[file] = new ResourceKey(path);
        parentFolder.Children.Add(file);
    }
}
