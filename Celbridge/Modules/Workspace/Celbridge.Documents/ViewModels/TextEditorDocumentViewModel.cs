using Celbridge.Entities;
using Celbridge.ExtensionAPI;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

using Path = System.IO.Path;

namespace Celbridge.Documents.ViewModels;

public partial class TextEditorDocumentViewModel : ObservableObject
{
    private readonly ILogger<TextEditorDocumentViewModel> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IDocumentsService _documentsService;
    private readonly IEntityService _entityService;

    private ResourceKey _fileResource;

    [ObservableProperty]
    private bool _showEditor = true;

    [ObservableProperty]
    private bool _showPreview = true;

    public TextEditorDocumentViewModel(
        ILogger<TextEditorDocumentViewModel> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;
    }

    public void SetFileResource(ResourceKey fileResource)
    {
        // SetFileResource() may be called multiple times if the resource is renamed.
        // Unregister the message handler first to avoid multiple registrations.
        _messengerService.Unregister<ComponentChangedMessage>(this);
        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);

        _fileResource = fileResource;

        UpdateEditorMode();
    }

    public Result<PreviewProvider> GetPreviewProvider()
    {
        var fileExtension = Path.GetExtension(_fileResource);
        if (string.IsNullOrEmpty(fileExtension))
        {
            return Result<PreviewProvider>.Fail();
        }

        var getResult = _documentsService.GetPreviewProvider(fileExtension);
        if (getResult.IsFailure)
        {
            return getResult;
        }

        var provider = getResult.Value;

        return Result<PreviewProvider>.Ok(provider);
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        if (message.Resource == _fileResource &&
            message.ComponentType == "Markdown" &&
            message.PropertyPath == MarkdownComponentConstants.EditorMode)
        {
            UpdateEditorMode();
        }
    }

    private void UpdateEditorMode()
    {
        try
        {
            // Get the editor mode from the markdown component.
            var editorMode = _entityService.GetProperty(_fileResource, "Markdown", MarkdownComponentConstants.EditorMode, EditorMode.Editor);

            ShowEditor = (editorMode == EditorMode.Editor || editorMode == EditorMode.EditorAndPreview);
            ShowPreview = (editorMode == EditorMode.Preview || editorMode == EditorMode.EditorAndPreview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
        }
    }
}
