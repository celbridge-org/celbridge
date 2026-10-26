using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Explorer.Menu;
using Celbridge.Explorer.Menu.Options;
using Celbridge.Resources;
using Celbridge.Search;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Unit tests for SearchInFolderMenuOption, which opens the Search panel on the clicked folder.
/// </summary>
[TestFixture]
public class SearchInFolderMenuOptionTests
{
    private static readonly ResourceItem ProjectFolder = new(ResourceKey.Empty, IsFolder: true);

    private ICommandService _commandService = null!;
    private SearchInFolderMenuOption _option = null!;

    [SetUp]
    public void Setup()
    {
        _commandService = Substitute.For<ICommandService>();
        _option = new SearchInFolderMenuOption(Substitute.For<IStringLocalizer>(), _commandService);
    }

    [Test]
    public void GetState_VisibleForAFolder()
    {
        var folder = new ResourceItem(new ResourceKey("docs"), IsFolder: true);

        var state = _option.GetState(ContextFor(folder));

        state.IsVisible.Should().BeTrue();
    }

    [Test]
    public void GetState_VisibleForTheProjectFolder()
    {
        var state = _option.GetState(ContextFor(ProjectFolder));

        state.IsVisible.Should().BeTrue();
    }

    [Test]
    public void GetState_HiddenForAFile()
    {
        var file = new ResourceItem(new ResourceKey("notes.md"), IsFolder: false);

        var state = _option.GetState(ContextFor(file));

        state.IsVisible.Should().BeFalse();
    }

    [Test]
    public void GetState_HiddenForSeveralSelectedFolders()
    {
        var docs = new ResourceItem(new ResourceKey("docs"), IsFolder: true);
        var src = new ResourceItem(new ResourceKey("src"), IsFolder: true);
        var context = ContextFor(docs) with
        {
            SelectedResources = new[]
            {
                docs,
                src
            },
        };

        var state = _option.GetState(context);

        state.IsVisible.Should().BeFalse();
    }

    [Test]
    public void Execute_ShowsTheSearchPanelOnTheFolder()
    {
        var command = Substitute.For<ISearchFolderCommand>();
        _commandService
            .When(service => service.Execute(Arg.Any<Action<ISearchFolderCommand>?>(), Arg.Any<string>(), Arg.Any<int>()))
            .Do(callInfo => callInfo.Arg<Action<ISearchFolderCommand>?>()?.Invoke(command));
        var folder = new ResourceItem(new ResourceKey("docs/api"), IsFolder: true);

        _option.Execute(ContextFor(folder));

        command.Received().Folder = new ResourceKey("docs/api");
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
}
