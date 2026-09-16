using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Explorer.Menu;
using Celbridge.Explorer.Menu.Options;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Unit tests for ArchiveMenuOption visibility. The option archives a single selected folder. The project
/// folder is offered for export by ExportArchiveMenuOption instead.
/// </summary>
[TestFixture]
public class ArchiveMenuOptionTests
{
    private IFolderResource _projectFolder = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolder = Substitute.For<IFolderResource>();
    }

    private static ArchiveMenuOption CreateOption()
    {
        return new ArchiveMenuOption(
            Substitute.For<IStringLocalizer>(),
            Substitute.For<ICommandService>(),
            Substitute.For<IWorkspaceWrapper>());
    }

    private ExplorerMenuContext ContextFor(IResource clickedResource)
    {
        var isProjectFolderTargeted = clickedResource == _projectFolder;

        IReadOnlyList<IResource> selectedResources = Array.Empty<IResource>();
        if (!isProjectFolderTargeted)
        {
            selectedResources = new[]
            {
                clickedResource
            };
        }

        return new ExplorerMenuContext(
            ClickedResource: clickedResource,
            SelectedResources: selectedResources,
            ProjectFolder: _projectFolder,
            IsProjectFolderTargeted: isProjectFolderTargeted,
            HasClipboardData: false,
            ClipboardContentType: ClipboardContentType.None,
            ClipboardOperation: ClipboardContentOperation.None);
    }

    [Test]
    public void GetState_HiddenWhenProjectFolderTargeted()
    {
        var state = CreateOption().GetState(ContextFor(_projectFolder));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void GetState_VisibleForSelectedFolder()
    {
        var folder = Substitute.For<IFolderResource>();

        var state = CreateOption().GetState(ContextFor(folder));

        state.IsVisible.Should().BeTrue();
        state.IsEnabled.Should().BeTrue();
    }

    [Test]
    public void GetState_HiddenForSelectedFile()
    {
        var file = Substitute.For<IFileResource>();

        var state = CreateOption().GetState(ContextFor(file));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }
}
