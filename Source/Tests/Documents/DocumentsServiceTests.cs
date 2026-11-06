using Celbridge.Commands;
using Celbridge.Documents.Helpers;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers the editor-selection surface on DocumentsService. OpenWithEditorAsync opens the file in the
/// chosen editor and records the choice in the sidecar once the document opens. The sidecar stores an
/// override only when the choice differs from the project default (the #746 self-shadow fix). The pick
/// lists badge the default and check the current editor. A stale override checks the default.
/// </summary>
[TestFixture]
public class DocumentsServiceTests
{
    private ICommandService _commandService = null!;
    private ITextBinarySniffer _textBinarySniffer = null!;
    private IServiceProvider _serviceProvider = null!;
    private IStringLocalizer _stringLocalizer = null!;
    private DocumentsService _documentsService = null!;
    private DocumentEditorRegistry _registry = null!;
    private ISidecarService _sidecarService = null!;
    private IDocumentsPanel _documentsPanel = null!;

    private IRemoveFieldsCommand? _capturedRemoveCommand;
    private ISetFieldsCommand? _capturedSetCommand;
    private IOpenDocumentCommand? _capturedOpenCommand;
    private Result<OpenDocumentOutcome> _openResult = null!;

    [SetUp]
    public void Setup()
    {
        _capturedRemoveCommand = null;
        _capturedSetCommand = null;
        _capturedOpenCommand = null;
        _openResult = Result<OpenDocumentOutcome>.Ok(OpenDocumentOutcome.Opened);

        _commandService = Substitute.For<ICommandService>();

        _commandService.ExecuteAsync<IOpenDocumentCommand, OpenDocumentOutcome>(
                Arg.Any<Action<IOpenDocumentCommand>>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IOpenDocumentCommand>>();
                var command = Substitute.For<IOpenDocumentCommand>();
                configure?.Invoke(command);
                _capturedOpenCommand = command;
                return Task.FromResult(_openResult);
            });

        // Capture the configured command by running the caller's configure action against a substitute,
        // so a test can assert what the service wrote. The trailing caller-info arguments are matched loosely.
        _commandService.ExecuteAsync<IRemoveFieldsCommand>(
                Arg.Any<Action<IRemoveFieldsCommand>>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<IRemoveFieldsCommand>>();
                var command = Substitute.For<IRemoveFieldsCommand>();
                configure?.Invoke(command);
                _capturedRemoveCommand = command;
                return Task.FromResult(Result.Ok());
            });

        _commandService.ExecuteAsync<ISetFieldsCommand>(
                Arg.Any<Action<ISetFieldsCommand>>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(callInfo =>
            {
                var configure = callInfo.Arg<Action<ISetFieldsCommand>>();
                var command = Substitute.For<ISetFieldsCommand>();
                configure?.Invoke(command);
                _capturedSetCommand = command;
                return Task.FromResult(Result.Ok());
            });

        _textBinarySniffer = Substitute.For<ITextBinarySniffer>();

        var messengerService = Substitute.For<IMessengerService>();

        var moduleService = Substitute.For<IModuleService>();
        moduleService.LoadedModules.Returns(new List<IModule>());

        _sidecarService = Substitute.For<ISidecarService>();
        _sidecarService.IsSidecarKey(Arg.Any<ResourceKey>()).Returns(false);
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Ok(
                new SidecarReadResult(SidecarReadOutcome.NoSidecar, null, null))));

        _documentsPanel = Substitute.For<IDocumentsPanel>();
        _documentsPanel.GetOpenDocuments().Returns(Array.Empty<OpenDocumentInfo>());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Sidecars.Returns(_sidecarService);
        workspaceService.DocumentsPanel.Returns(_documentsPanel);

        // The service guards that the workspace page is not yet loaded during construction.
        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(false);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _stringLocalizer["OpenWith_DefaultFormat", Arg.Any<object[]>()]
            .Returns(callInfo =>
            {
                var arguments = (object[])callInfo[1];
                return new LocalizedString("OpenWith_DefaultFormat", $"{arguments[0]} (default)");
            });

        _serviceProvider = Substitute.For<IServiceProvider>();
        _serviceProvider.GetService(typeof(ILogger<DocumentEditorPreferenceStore>))
            .Returns(Substitute.For<ILogger<DocumentEditorPreferenceStore>>());
        _serviceProvider.GetService(typeof(ILogger<DocumentViewFactory>))
            .Returns(Substitute.For<ILogger<DocumentViewFactory>>());
        _serviceProvider.GetService(typeof(ILogger<DocumentLayoutStore>))
            .Returns(Substitute.For<ILogger<DocumentLayoutStore>>());
        _serviceProvider.GetService(typeof(IStringLocalizer)).Returns(_stringLocalizer);

        _documentsService = new DocumentsService(
            _serviceProvider,
            Substitute.For<ILogger<DocumentsService>>(),
            messengerService,
            _commandService,
            moduleService,
            workspaceWrapper,
            _textBinarySniffer);

        _registry = (DocumentEditorRegistry)_documentsService.DocumentEditorRegistry;
    }

    [Test]
    public async Task OpenWithEditorAsync_ChoosingTheProjectDefault_ClearsTheSidecarOverride()
    {
        // The single registered editor is the project default, so choosing it must clear the sidecar
        // rather than write a redundant override that would shadow the default (the #746 fix).
        var defaultEditorId = new EditorId("test.default-editor");
        _registry.RegisterFactory(CreateFactory(defaultEditorId, ".md", "Default Editor"));

        var result = await _documentsService.OpenWithEditorAsync(
            new ResourceKey("doc.md"), defaultEditorId);

        result.IsSuccess.Should().BeTrue();
        _capturedRemoveCommand.Should().NotBeNull();
        _capturedRemoveCommand!.Resource.Should().Be(new ResourceKey("doc.md"));
        _capturedRemoveCommand.Names.Should().Contain(SidecarFieldNames.Editor);
        _capturedSetCommand.Should().BeNull();
    }

    [Test]
    public async Task OpenWithEditorAsync_ChoosingAnotherEditor_OpensTheFileInItAndWritesTheOverride()
    {
        var defaultEditorId = new EditorId("test.default-editor");
        var chosenEditorId = new EditorId("test.other-editor");
        _registry.RegisterFactory(CreateFactory(defaultEditorId, ".md", "Default Editor"));

        var result = await _documentsService.OpenWithEditorAsync(
            new ResourceKey("doc.md"), chosenEditorId);

        result.IsSuccess.Should().BeTrue();
        _capturedOpenCommand.Should().NotBeNull();
        _capturedOpenCommand!.FileResource.Should().Be(new ResourceKey("doc.md"));
        _capturedOpenCommand.EditorId.Should().Be(chosenEditorId);
        _capturedSetCommand.Should().NotBeNull();
        _capturedSetCommand!.Resource.Should().Be(new ResourceKey("doc.md"));
        _capturedSetCommand.Fields[SidecarFieldNames.Editor].Should().Be(chosenEditorId.ToString());
        _capturedRemoveCommand.Should().BeNull();
    }

    [Test]
    public async Task OpenWithEditorAsync_AReservedFileType_OpensTheFileAndRecordsNothing()
    {
        // A reserved type opens in another editor for a look, but the look is not persisted: no sidecar
        // is written beside a file the application depends on, and none is cleared either.
        var reservingEditorId = new EditorId("test.reserving-editor");
        var factory = CreateFactory(reservingEditorId, ".celbridge", "Reserving Editor");
        factory.ReservesFileType.Returns(true);
        _registry.RegisterFactory(factory);

        var result = await _documentsService.OpenWithEditorAsync(
            new ResourceKey("Acme.celbridge"), new EditorId("test.code-editor"));

        result.IsSuccess.Should().BeTrue();
        _capturedOpenCommand.Should().NotBeNull();
        _capturedSetCommand.Should().BeNull();
        _capturedRemoveCommand.Should().BeNull();
    }

    [Test]
    public async Task OpenWithEditorAsync_AFileOutsideTheProject_OpensTheFileAndRecordsNothing()
    {
        // Sidecars belong to project files, so a generated temp: page or any other file outside the project
        // root has nowhere to record the choice. Trying would fail the call after the file had opened.
        _registry.RegisterFactory(CreateFactory(new EditorId("test.default-editor"), ".webview", "Default Editor"));

        var result = await _documentsService.OpenWithEditorAsync(
            new ResourceKey("temp:community.webview"), new EditorId("test.other-editor"));

        result.IsSuccess.Should().BeTrue();
        _capturedOpenCommand.Should().NotBeNull();
        _capturedSetCommand.Should().BeNull();
        _capturedRemoveCommand.Should().BeNull();
    }

    [Test]
    public async Task OpenWithEditorAsync_ADocumentThatRefusesToClose_RecordsNothing()
    {
        // The open document refuses to close and stays in the current editor. Recording the choice would
        // open the file in the other editor next time.
        _registry.RegisterFactory(CreateFactory(new EditorId("test.default-editor"), ".md", "Default Editor"));
        _openResult = Result<OpenDocumentOutcome>.Ok(OpenDocumentOutcome.Cancelled);

        var result = await _documentsService.OpenWithEditorAsync(
            new ResourceKey("doc.md"), new EditorId("test.other-editor"));

        result.IsSuccess.Should().BeTrue();
        _capturedSetCommand.Should().BeNull();
        _capturedRemoveCommand.Should().BeNull();
    }

    [Test]
    public async Task OpenWithEditorAsync_AFailedOpen_FailsAndRecordsNothing()
    {
        _registry.RegisterFactory(CreateFactory(new EditorId("test.default-editor"), ".md", "Default Editor"));
        _openResult = Result<OpenDocumentOutcome>.Fail("The editor could not create its view");

        var result = await _documentsService.OpenWithEditorAsync(
            new ResourceKey("doc.md"), new EditorId("test.other-editor"));

        result.IsFailure.Should().BeTrue();
        _capturedSetCommand.Should().BeNull();
        _capturedRemoveCommand.Should().BeNull();
    }

    [Test]
    public void GetEditorsForDocument_ReturnsNullWhenFewerThanTwoEditors()
    {
        _registry.RegisterFactory(CreateFactory(new EditorId("test.only"), ".md", "Only Editor"));

        var pickList = _documentsService.GetEditorsForDocument(new ResourceKey("doc.md"), EditorId.Empty);

        pickList.Should().BeNull();
    }

    [Test]
    public void GetEditorsForDocument_BadgesDefaultAndChecksCurrentEditor()
    {
        // The first-registered editor is the project default; the current editor is the second one.
        var editorA = new EditorId("test.editor-a");
        var editorB = new EditorId("test.editor-b");
        _registry.RegisterFactory(CreateFactory(editorA, ".md", "Editor A"));
        _registry.RegisterFactory(CreateFactory(editorB, ".md", "Editor B"));

        var pickList = _documentsService.GetEditorsForDocument(new ResourceKey("doc.md"), editorB);

        pickList.Should().NotBeNull();
        pickList!.EditorIds.Should().Equal(editorA, editorB);
        pickList.Labels[0].Should().Be("Editor A (default)");
        pickList.Labels[1].Should().Be("Editor B");
        pickList.CurrentIndex.Should().Be(1);
    }

    [Test]
    public void GetEditorsForDocument_ChecksDefaultWhenCurrentEditorIsStale()
    {
        // The current id names an editor that is no longer a candidate (a stale sidecar), so the
        // check falls back to the project default at index 0.
        var editorA = new EditorId("test.editor-a");
        var editorB = new EditorId("test.editor-b");
        _registry.RegisterFactory(CreateFactory(editorA, ".md", "Editor A"));
        _registry.RegisterFactory(CreateFactory(editorB, ".md", "Editor B"));

        var pickList = _documentsService.GetEditorsForDocument(
            new ResourceKey("doc.md"), new EditorId("test.uninstalled"));

        pickList.Should().NotBeNull();
        pickList!.CurrentIndex.Should().Be(0);
    }

    [Test]
    public async Task GetEditorsForFileAsync_ChecksTheEditorOfTheOpenDocument()
    {
        var editorA = new EditorId("test.editor-a");
        var editorB = new EditorId("test.editor-b");
        _registry.RegisterFactory(CreateFactory(editorA, ".md", "Editor A"));
        _registry.RegisterFactory(CreateFactory(editorB, ".md", "Editor B"));
        var fileResource = new ResourceKey("doc.md");
        var address = new DocumentAddress(0, DocumentSection.MainLeft, 0);
        _documentsPanel.GetOpenDocuments().Returns(new[] { new OpenDocumentInfo(fileResource, address, editorB) });

        var pickList = await _documentsService.GetEditorsForFileAsync(fileResource);

        pickList.Should().NotBeNull();
        pickList!.CurrentIndex.Should().Be(1);
    }

    [Test]
    public async Task GetEditorsForFileAsync_ChecksTheOverrideOfAClosedFile()
    {
        var editorA = new EditorId("test.editor-a");
        var editorB = new EditorId("test.editor-b");
        _registry.RegisterFactory(CreateFactory(editorA, ".md", "Editor A"));
        _registry.RegisterFactory(CreateFactory(editorB, ".md", "Editor B"));
        StubSidecarEditor(editorB);

        var pickList = await _documentsService.GetEditorsForFileAsync(new ResourceKey("doc.md"));

        pickList.Should().NotBeNull();
        pickList!.CurrentIndex.Should().Be(1);
    }

    [Test]
    public async Task GetEditorsForFileAsync_ASingleEditor_ReturnsNullWithoutReadingTheSidecar()
    {
        _registry.RegisterFactory(CreateFactory(new EditorId("test.only"), ".md", "Only Editor"));

        var pickList = await _documentsService.GetEditorsForFileAsync(new ResourceKey("doc.md"));

        pickList.Should().BeNull();
        await _sidecarService.DidNotReceive().ReadAsync(Arg.Any<ResourceKey>());
    }

    [Test]
    public void IsReservedFileType_ExtensionClaimedByAReservingEditor_IsReserved()
    {
        // The Project Settings editor reserves .celbridge, so the File Types page leaves it out rather
        // than offering to point it at a different editor.
        var factory = CreateFactory(new EditorId("test.reserving-editor"), ".celbridge", "Reserving Editor");
        factory.ReservesFileType.Returns(true);
        _registry.RegisterFactory(factory);

        _documentsService.IsReservedFileType(".celbridge").Should().BeTrue();
    }

    [Test]
    public void IsReservedFileType_OrdinaryExtension_IsNotReserved()
    {
        _registry.RegisterFactory(CreateFactory(new EditorId("test.md-editor"), ".md", "Markdown Editor"));

        _documentsService.IsReservedFileType(".md").Should().BeFalse();
    }

    [Test]
    public void IsReservedFileType_ParentOfAReservedMultiPartExtension_IsNotReserved()
    {
        // The reservation is on the exact extension: ".editor.toml" carries a package role while ".toml"
        // is an ordinary text file the user may reassign.
        var factory = CreateFactory(new EditorId("test.manifest"), ".editor.toml", "Editor Manifest");
        factory.ReservesFileType.Returns(true);
        _registry.RegisterFactory(factory);

        _documentsService.IsReservedFileType(".toml").Should().BeFalse();
    }

    private void StubSidecarEditor(EditorId editorId)
    {
        var fields = new Dictionary<string, object>
        {
            [SidecarFieldNames.Editor] = editorId.ToString(),
        };
        var content = new SidecarContent(fields);
        _sidecarService.ReadAsync(Arg.Any<ResourceKey>())
            .Returns(Task.FromResult(Result<SidecarReadResult>.Ok(
                new SidecarReadResult(SidecarReadOutcome.Healthy, content, null))));
    }

    private static IDocumentEditorFactory CreateFactory(
        EditorId editorId,
        string extension,
        string displayName)
    {
        var factory = Substitute.For<IDocumentEditorFactory>();
        factory.EditorId.Returns(editorId);
        factory.DisplayName.Returns(displayName);
        factory.SupportedExtensions.Returns(new List<string> { extension });
        factory.IsPlaceholder.Returns(false);
        factory.CanHandleResource(Arg.Any<ResourceKey>()).Returns(true);
        return factory;
    }
}
