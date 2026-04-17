using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Explorer.Menu;
using Celbridge.Explorer.Menu.Options;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Unit tests for OpenWithMenuOption's visibility / display logic.
/// The Execute path is async void with substantial UI state and dialog interaction, so it's
/// better exercised by manual testing or integration tests rather than mocked unit tests.
/// </summary>
[TestFixture]
public class OpenWithMenuOptionTests
{
    private IStringLocalizer _stringLocalizer = null!;
    private ICommandService _commandService = null!;
    private IDialogService _dialogService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IDocumentEditorRegistry _editorRegistry = null!;
    private Logging.ILogger<OpenWithMenuOption> _logger = null!;

    [SetUp]
    public void Setup()
    {
        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _commandService = Substitute.For<ICommandService>();
        _dialogService = Substitute.For<IDialogService>();
        _logger = Substitute.For<Logging.ILogger<OpenWithMenuOption>>();

        _editorRegistry = Substitute.For<IDocumentEditorRegistry>();

        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.DocumentEditorRegistry.Returns(_editorRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(documentsService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    private OpenWithMenuOption CreateOption()
    {
        return new OpenWithMenuOption(
            _stringLocalizer,
            _commandService,
            _workspaceWrapper,
            _dialogService,
            _logger);
    }

    private static ExplorerMenuContext ContextFor(IResource? clickedResource)
    {
        var rootFolder = Substitute.For<IFolderResource>();
        return new ExplorerMenuContext(
            ClickedResource: clickedResource,
            SelectedResources: clickedResource is null ? Array.Empty<IResource>() : new[] { clickedResource },
            RootFolder: rootFolder,
            IsRootFolderTargeted: false,
            HasClipboardData: false,
            ClipboardContentType: ClipboardContentType.None,
            ClipboardOperation: ClipboardContentOperation.None);
    }

    private static IFileResource CreateFileResource(string name)
    {
        var file = Substitute.For<IFileResource>();
        file.Name.Returns(name);
        return file;
    }

    [Test]
    public void GetState_HiddenWhenNoFileClicked()
    {
        var option = CreateOption();
        var folder = Substitute.For<IFolderResource>();

        var state = option.GetState(ContextFor(folder));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void GetState_HiddenWhenFewerThanTwoEditorsRegistered()
    {
        var clickedFile = CreateFileResource("readme.md");
        var singleFactory = Substitute.For<IDocumentEditorFactory>();
        _editorRegistry.GetFactoriesForFileExtension(".md").Returns(new[] { singleFactory });

        var option = CreateOption();
        var state = option.GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void GetState_VisibleWhenMultipleEditorsRegistered()
    {
        var clickedFile = CreateFileResource("readme.md");
        var firstFactory = Substitute.For<IDocumentEditorFactory>();
        var secondFactory = Substitute.For<IDocumentEditorFactory>();
        _editorRegistry.GetFactoriesForFileExtension(".md").Returns(new[] { firstFactory, secondFactory });

        var option = CreateOption();
        var state = option.GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeTrue();
        state.IsEnabled.Should().BeTrue();
    }

    [Test]
    public void GetState_NormalisesExtensionToLowercase()
    {
        var clickedFile = CreateFileResource("README.MD");
        var firstFactory = Substitute.For<IDocumentEditorFactory>();
        var secondFactory = Substitute.For<IDocumentEditorFactory>();

        // Only the lowercase ".md" lookup is wired up. If the option doesn't lowercase the extension
        // before querying the registry, this test fails because the default Substitute returns null.
        _editorRegistry.GetFactoriesForFileExtension(".md").Returns(new[] { firstFactory, secondFactory });

        var option = CreateOption();
        var state = option.GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeTrue();
    }
}
