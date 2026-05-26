using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Explorer.Menu;
using Celbridge.Explorer.Menu.Options;
using Celbridge.Resources;
using Celbridge.Utilities;
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
    private IDocumentsService _documentsService = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private Logging.ILogger<OpenWithMenuOption> _logger = null!;

    [SetUp]
    public void Setup()
    {
        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _commandService = Substitute.For<ICommandService>();
        _dialogService = Substitute.For<IDialogService>();
        _logger = Substitute.For<Logging.ILogger<OpenWithMenuOption>>();

        _editorRegistry = Substitute.For<IDocumentEditorRegistry>();
        // Default to an empty candidate list; tests opt-in by stubbing this.
        _editorRegistry.GetUserPickableFactoriesForResource(Arg.Any<ResourceKey>())
            .Returns(Array.Empty<IDocumentEditorFactory>());

        _documentsService = Substitute.For<IDocumentsService>();
        _documentsService.DocumentEditorRegistry.Returns(_editorRegistry);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.GetResourceKey(Arg.Any<IResource>())
            .Returns(callInfo => new ResourceKey(((IResource)callInfo[0]).Name));

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(_documentsService);
        workspaceService.ResourceService.Returns(resourceService);

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
        var projectFolder = Substitute.For<IFolderResource>();
        return new ExplorerMenuContext(
            ClickedResource: clickedResource,
            SelectedResources: clickedResource is null ? Array.Empty<IResource>() : new[] { clickedResource },
            ProjectFolder: projectFolder,
            IsProjectFolderTargeted: false,
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

    private static IDocumentEditorFactory CreateFactory(string editorId)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(new DocumentEditorId(editorId));
        return factory;
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
        var singleFactory = CreateFactory("acme.md-only");
        _editorRegistry.GetUserPickableFactoriesForResource(Arg.Any<ResourceKey>())
            .Returns(new[] { singleFactory });

        var option = CreateOption();
        var state = option.GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeFalse();
        state.IsEnabled.Should().BeFalse();
    }

    [Test]
    public void GetState_VisibleWhenMultipleEditorsRegistered()
    {
        var clickedFile = CreateFileResource("readme.md");
        var firstFactory = CreateFactory("acme.markdown");
        var secondFactory = CreateFactory("acme.code");
        _editorRegistry.GetUserPickableFactoriesForResource(Arg.Any<ResourceKey>())
            .Returns(new[] { firstFactory, secondFactory });

        var option = CreateOption();
        var state = option.GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeTrue();
        state.IsEnabled.Should().BeTrue();
    }

    [Test]
    public void GetState_VisibleForMultiPartExtensionWithSingleSpecializedEditorPlusFallback()
    {
        // Registry returns the specialized editor plus the code editor fallback;
        // two candidates make the menu visible.
        var clickedFile = CreateFileResource("design.widget.cel");
        var specializedEditor = CreateFactory("acme.widget-editor.widget-document");
        var fallback = CreateFactory("celbridge.code-editor.code-document");
        _editorRegistry.GetUserPickableFactoriesForResource(Arg.Any<ResourceKey>())
            .Returns(new[] { specializedEditor, fallback });

        var option = CreateOption();
        var state = option.GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeTrue();
        state.IsEnabled.Should().BeTrue();
    }

    [Test]
    public void GetState_HiddenForBinaryFileWithSingleSpecializedEditor()
    {
        // For binary files (.png, .pdf, .zip, etc.) the text fallback must not be
        // offered: Monaco would just show garbled bytes. With only one specialized
        // editor registered and the fallback suppressed, no second candidate
        // remains, so the menu stays hidden.
        var clickedFile = CreateFileResource("photo.png");
        var specializedEditor = CreateFactory("acme.binary-editor");
        _editorRegistry.GetUserPickableFactoriesForResource(Arg.Any<ResourceKey>())
            .Returns(new[] { specializedEditor });

        var fallback = CreateFactory("celbridge.code-editor.code-document");
        _editorRegistry.GetFactoryById(DocumentConstants.CodeEditorId)
            .Returns(Result<IDocumentEditorFactory>.Ok(fallback));

        var option = CreateOption();
        var state = option.GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeFalse();
    }

    [Test]
    public void GetState_VisibleForBinaryFileWithMultipleSpecializedEditors()
    {
        // When two or more specialized editors claim a binary file, the menu is
        // visible because two real candidates exist. The fallback skip for binary
        // files does not prevent the menu showing in this case.
        var clickedFile = CreateFileResource("photo.png");
        var firstEditor = CreateFactory("acme.binary-editor-one");
        var secondEditor = CreateFactory("acme.binary-editor-two");
        _editorRegistry.GetUserPickableFactoriesForResource(Arg.Any<ResourceKey>())
            .Returns(new[] { firstEditor, secondEditor });

        var fallback = CreateFactory("celbridge.code-editor.code-document");
        _editorRegistry.GetFactoryById(DocumentConstants.CodeEditorId)
            .Returns(Result<IDocumentEditorFactory>.Ok(fallback));

        var option = CreateOption();
        var state = option.GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeTrue();
    }

    [Test]
    public void GetState_HiddenWhenOnlyCandidateIsTextFallback()
    {
        // No specialized editor registers for the extension. The fallback alone is
        // a single candidate, which is not enough to show the menu (the user would
        // have nothing to choose between).
        var clickedFile = CreateFileResource("scratch.xyz");
        _editorRegistry.GetUserPickableFactoriesForResource(Arg.Any<ResourceKey>())
            .Returns(Array.Empty<IDocumentEditorFactory>());

        var fallback = CreateFactory("celbridge.code-editor.code-document");
        _editorRegistry.GetFactoryById(DocumentConstants.CodeEditorId)
            .Returns(Result<IDocumentEditorFactory>.Ok(fallback));

        var option = CreateOption();
        var state = option.GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeFalse();
    }

    [Test]
    public void GetState_HiddenForPlaceholderFactoryPlusTextFallback()
    {
        // Placeholder factories (PackageManifestFactory, ProjectFileFactory,
        // DocumentContributionFactory) exist only to register an extension for
        // resource classification; they cannot create document views and must
        // not appear in the "Open with..." picker. With one placeholder plus
        // the text fallback, only the fallback survives the filter, so the
        // menu stays hidden (one candidate, nothing to pick between). This
        // closes the footgun where picking a placeholder would write a
        // non-functional editor id into the manifest's own frontmatter.
        var clickedFile = CreateFileResource("package.cel");
        var placeholder = CreateFactory("celbridge.package-manifest");
        placeholder.IsPlaceholder.Returns(true);
        _editorRegistry.GetUserPickableFactoriesForResource(Arg.Any<ResourceKey>())
            .Returns(new[] { placeholder });

        var fallback = CreateFactory("celbridge.code-editor.code-document");
        _editorRegistry.GetFactoryById(DocumentConstants.CodeEditorId)
            .Returns(Result<IDocumentEditorFactory>.Ok(fallback));

        var option = CreateOption();
        var state = option.GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeFalse();
    }

    [Test]
    public void GetState_DoesNotDuplicateTextFallbackWhenAlreadyRegistered()
    {
        // The code editor registers itself explicitly for .md (alongside the
        // markdown preview editor). The augmentation must dedupe by editor id,
        // otherwise the dialog would show two "Source Code Editor" entries.
        var clickedFile = CreateFileResource("readme.md");
        var fallback = CreateFactory("celbridge.code-editor.code-document");

        // The registry returns only the fallback, simulating an extension where the
        // code editor is the sole registered factory. Without dedup, the augmented
        // list would have two copies and falsely report >= 2 candidates.
        _editorRegistry.GetUserPickableFactoriesForResource(Arg.Any<ResourceKey>())
            .Returns(new[] { fallback });
        _editorRegistry.GetFactoryById(DocumentConstants.CodeEditorId)
            .Returns(Result<IDocumentEditorFactory>.Ok(fallback));

        var option = CreateOption();
        var state = option.GetState(ContextFor(clickedFile));

        state.IsVisible.Should().BeFalse();
    }
}
