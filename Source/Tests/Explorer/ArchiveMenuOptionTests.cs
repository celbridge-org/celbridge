using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Explorer.Menu;
using Celbridge.Explorer.Menu.Options;
using Celbridge.Resources;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Unit tests for ArchiveMenuOption visibility. The option archives a single selected folder. The project
/// folder is offered for export by ExportArchiveMenuOption instead.
/// </summary>
[TestFixture]
public class ArchiveMenuOptionTests
{
    private static readonly ResourceItem ProjectFolder = new(ResourceKey.Empty, IsFolder: true);

    private static ArchiveMenuOption CreateOption()
    {
        return new ArchiveMenuOption(
            Substitute.For<IStringLocalizer>(),
            Substitute.For<ICommandService>());
    }

    private static ExplorerMenuContext ContextFor(ResourceItem clickedResource)
    {
        var isProjectFolderTargeted = clickedResource.Resource.IsEmpty;

        IReadOnlyList<ResourceItem> selectedResources = Array.Empty<ResourceItem>();
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
            IsProjectFolderTargeted: isProjectFolderTargeted,
            HasClipboardData: false,
            ClipboardContentType: ClipboardContentType.None,
            ClipboardOperation: ClipboardContentOperation.None);
    }

    [Test]
    public void GetState_HiddenWhenProjectFolderTargeted()
    {
        var state = CreateOption().GetState(ContextFor(ProjectFolder));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void GetState_VisibleForSelectedFolder()
    {
        var folder = new ResourceItem(new ResourceKey("Docs"), IsFolder: true);

        var state = CreateOption().GetState(ContextFor(folder));

        state.IsVisible.Should().BeTrue();
        state.IsEnabled.Should().BeTrue();
    }

    [Test]
    public void GetState_HiddenForSelectedFile()
    {
        var file = new ResourceItem(new ResourceKey("notes.md"), IsFolder: false);

        var state = CreateOption().GetState(ContextFor(file));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }
}
