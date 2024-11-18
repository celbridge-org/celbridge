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
        _messengerService.Unregister<EntityChangedMessage>(this);
        _messengerService.Register<EntityChangedMessage>(this, OnEntityChangedMessage);

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

    private void OnEntityChangedMessage(object recipient, EntityChangedMessage message)
    {
        var (resource, paths) = message;

        if (resource != _fileResource)
        {
            return;
        }

        if (paths.Contains(TextEditorEntityConstants.EditorMode))
        {
            UpdateEditorMode();
        }
    }

    private void UpdateEditorMode()
    {
        try
        {
            var editorMode = _entityService.GetProperty(_fileResource, TextEditorEntityConstants.EditorMode, EditorMode.Editor);

            ShowEditor = (editorMode == EditorMode.Editor || editorMode == EditorMode.EditorAndPreview);
            ShowPreview = (editorMode == EditorMode.Preview || editorMode == EditorMode.EditorAndPreview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
        }
    }
}
