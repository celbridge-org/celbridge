using Celbridge.DataTransfer;
using Celbridge.Explorer.Menu;
using Celbridge.Explorer.Menu.Options;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Unit tests for OpenWithMenuOption's visibility, its submenu of editors, and choosing an editor.
/// </summary>
[TestFixture]
public class OpenWithMenuOptionTests
{
    private IStringLocalizer _stringLocalizer = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IDocumentsService _documentsService = null!;
    private Logging.ILogger<OpenWithMenuOption> _logger = null!;

    private static readonly EditorId MarkdownEditor = new("acme.markdown");
    private static readonly EditorId CodeEditor = new("acme.code");

    [SetUp]
    public void Setup()
    {
        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _logger = Substitute.For<Logging.ILogger<OpenWithMenuOption>>();

        _documentsService = Substitute.For<IDocumentsService>();
        _documentsService.OpenWithEditorAsync(Arg.Any<ResourceKey>(), Arg.Any<EditorId>())
            .Returns(Task.FromResult(Result.Ok()));

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(_documentsService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    private OpenWithMenuOption CreateOption()
    {
        return new OpenWithMenuOption(
            _stringLocalizer,
            _workspaceWrapper,
            _logger);
    }

    private static ExplorerMenuContext ContextFor(ResourceItem clickedResource, EditorPickList? openWithChoices = null)
    {
        var selectedResources = new[]
        {
            clickedResource
        };

        return new ExplorerMenuContext(
            ClickedResource: clickedResource,
            SelectedResources: selectedResources,
            IsProjectFolderTargeted: false,
            HasClipboardData: false,
            ClipboardContentType: ClipboardContentType.None,
            ClipboardOperation: ClipboardContentOperation.None)
        {
            OpenWithChoices = openWithChoices,
        };
    }

    private static ResourceItem CreateFileResource(string name)
    {
        return new ResourceItem(new ResourceKey(name), IsFolder: false);
    }

    private static EditorPickList CreateChoices(int currentIndex)
    {
        return new EditorPickList(
            new[] { MarkdownEditor, CodeEditor },
            new[] { "Markdown (default)", "Code" },
            currentIndex);
    }

    [Test]
    public void GetState_HiddenForAFolder()
    {
        var folder = new ResourceItem(new ResourceKey("Docs"), IsFolder: true);

        var state = CreateOption().GetState(ContextFor(folder));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void GetState_HiddenForAFileWithoutAChoiceOfEditors()
    {
        var state = CreateOption().GetState(ContextFor(CreateFileResource("readme.md")));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void GetState_VisibleForAFileWithAChoiceOfEditors()
    {
        var context = ContextFor(CreateFileResource("readme.md"), CreateChoices(currentIndex: 0));

        var state = CreateOption().GetState(context);

        state.IsVisible.Should().BeTrue();
        state.IsEnabled.Should().BeTrue();
    }

    [Test]
    public void GetSubMenuItems_ListsEachEditorAndChecksTheCurrentOne()
    {
        var context = ContextFor(CreateFileResource("readme.md"), CreateChoices(currentIndex: 1));

        var items = CreateOption().GetSubMenuItems(context);

        items.Select(item => item.LocalizedText).Should().Equal("Markdown (default)", "Code");
        items.Select(item => item.IsChecked).Should().Equal(false, true);
    }

    [Test]
    public void ChoosingAnEditor_OpensTheFileInIt()
    {
        var clickedFile = CreateFileResource("readme.md");
        var context = ContextFor(clickedFile, CreateChoices(currentIndex: 0));

        var items = CreateOption().GetSubMenuItems(context);
        items[1].Execute();

        _documentsService.Received(1).OpenWithEditorAsync(clickedFile.Resource, CodeEditor);
    }
}
