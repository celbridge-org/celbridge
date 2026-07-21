using Celbridge.Commands;
using Celbridge.Documents.Helpers;
using Celbridge.Messaging;
using Celbridge.Modules;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers the editor-selection surface on DocumentsService: SetPreferredEditorAsync writes the sidecar
/// override only when the choice deviates from the project default and clears it otherwise (the #746
/// self-shadow fix), and GetEditorPickList badges the default, preselects the effective editor, and
/// falls back to the default for a stale override.
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

    private IRemoveFieldsCommand? _capturedRemoveCommand;
    private ISetFieldsCommand? _capturedSetCommand;

    [SetUp]
    public void Setup()
    {
        _capturedRemoveCommand = null;
        _capturedSetCommand = null;

        _commandService = Substitute.For<ICommandService>();

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

        // The service guards that the workspace page is not yet loaded during construction.
        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _stringLocalizer["OpenWithDialog_DefaultFormat", Arg.Any<object[]>()]
            .Returns(callInfo =>
            {
                var arguments = (object[])callInfo[1];
                return new LocalizedString("OpenWithDialog_DefaultFormat", $"{arguments[0]} (default)");
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
    public async Task SetPreferredEditorAsync_ChoiceEqualsProjectDefault_ClearsSidecarOverride()
    {
        // The single registered editor is the project default, so choosing it must clear the sidecar
        // rather than write a redundant override that would shadow the default (the #746 fix).
        var defaultEditorId = new EditorInstanceId("test.default-editor");
        _registry.RegisterFactory(CreateFactory(defaultEditorId, ".md", "Default Editor"));

        var result = await _documentsService.SetPreferredEditorAsync(
            new ResourceKey("doc.md"), defaultEditorId);

        result.IsSuccess.Should().BeTrue();
        _capturedRemoveCommand.Should().NotBeNull();
        _capturedRemoveCommand!.Resource.Should().Be(new ResourceKey("doc.md"));
        _capturedRemoveCommand.Names.Should().Contain(SidecarFieldNames.Editor);
        _capturedSetCommand.Should().BeNull();
    }

    [Test]
    public async Task SetPreferredEditorAsync_ChoiceDiffersFromDefault_WritesSidecarOverride()
    {
        var defaultEditorId = new EditorInstanceId("test.default-editor");
        var chosenEditorId = new EditorInstanceId("test.other-editor");
        _registry.RegisterFactory(CreateFactory(defaultEditorId, ".md", "Default Editor"));

        var result = await _documentsService.SetPreferredEditorAsync(
            new ResourceKey("doc.md"), chosenEditorId);

        result.IsSuccess.Should().BeTrue();
        _capturedSetCommand.Should().NotBeNull();
        _capturedSetCommand!.Resource.Should().Be(new ResourceKey("doc.md"));
        _capturedSetCommand.Fields[SidecarFieldNames.Editor].Should().Be(chosenEditorId.ToString());
        _capturedRemoveCommand.Should().BeNull();
    }

    [Test]
    public void GetEditorPickList_ReturnsNullWhenFewerThanTwoEditors()
    {
        _registry.RegisterFactory(CreateFactory(new EditorInstanceId("test.only"), ".md", "Only Editor"));

        var pickList = _documentsService.GetEditorPickList(new ResourceKey("doc.md"), EditorInstanceId.Empty);

        pickList.Should().BeNull();
    }

    [Test]
    public void GetEditorPickList_BadgesDefaultAndPreselectsCurrentEditor()
    {
        // The first-registered editor is the project default; the current editor is the second one.
        var editorA = new EditorInstanceId("test.editor-a");
        var editorB = new EditorInstanceId("test.editor-b");
        _registry.RegisterFactory(CreateFactory(editorA, ".md", "Editor A"));
        _registry.RegisterFactory(CreateFactory(editorB, ".md", "Editor B"));

        var pickList = _documentsService.GetEditorPickList(new ResourceKey("doc.md"), editorB);

        pickList.Should().NotBeNull();
        pickList!.EditorIds.Should().Equal(editorA, editorB);
        pickList.Labels[0].Should().Be("Editor A (default)");
        pickList.Labels[1].Should().Be("Editor B");
        pickList.SelectedIndex.Should().Be(1);
    }

    [Test]
    public void GetEditorPickList_PreselectsDefaultWhenCurrentEditorIsStale()
    {
        // The current id names an editor that is no longer a candidate (a stale sidecar), so the
        // preselection falls back to the project default at index 0.
        var editorA = new EditorInstanceId("test.editor-a");
        var editorB = new EditorInstanceId("test.editor-b");
        _registry.RegisterFactory(CreateFactory(editorA, ".md", "Editor A"));
        _registry.RegisterFactory(CreateFactory(editorB, ".md", "Editor B"));

        var pickList = _documentsService.GetEditorPickList(
            new ResourceKey("doc.md"), new EditorInstanceId("test.uninstalled"));

        pickList.Should().NotBeNull();
        pickList!.SelectedIndex.Should().Be(0);
    }

    private static IDocumentEditorFactory CreateFactory(
        EditorInstanceId editorId,
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
