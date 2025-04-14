using Celbridge.Documents.Services;
using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Nodes;
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

    public Action<string>? OnSetContent;

    public TextEditorDocumentViewModel(
        ILogger<TextEditorDocumentViewModel> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;

        _messengerService.Register<SetTextDocumentContentMessage>(this, OnSetTextDocumentContentMessage);
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

    public Result<IPreviewProvider> GetPreviewProvider()
    {
        var fileExtension = Path.GetExtension(_fileResource);
        if (string.IsNullOrEmpty(fileExtension))
        {
            return Result<IPreviewProvider>.Fail();
        }

        var getResult = _documentsService.GetPreviewProvider(fileExtension);
        if (getResult.IsFailure)
        {
            return getResult;
        }

        var provider = getResult.Value;

        return Result<IPreviewProvider>.Ok(provider);
    }

    private void OnSetTextDocumentContentMessage(object recipient, SetTextDocumentContentMessage message)
    {
        if (message.Resource != _fileResource)
        {
            return;
        }

        var content = message.Content;

        // Notify the view that the content should be updated
        OnSetContent?.Invoke(content);
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        if (message.ComponentKey.Resource == _fileResource &&
            message.ComponentKey.ComponentIndex == 0)
        {
            UpdateEditorMode();
        }
    }

    private void UpdateEditorMode()
    {
        try
        {
            var editorMode = EditorMode.Editor;

            // Any root component may specify the "/editorMode" property, but it must have the
            // "isPreviewController" attribute set to "true". This is roughly equivalent to implementing an
            // interface in a language like C#.

            var componentKey = new ComponentKey(_fileResource, 0);
            var getComponentResult = _entityService.GetComponent(componentKey);
            if (getComponentResult.IsSuccess)
            {
                var component = getComponentResult.Value;

                var isPreviewController = component.Schema.GetBooleanAttribute("isPreviewController");

                if (isPreviewController)
                {
                    // Get the editor mode property from the root component.
                    var getPropertyResult = component.GetProperty(DocumentConstants.EditorModeProperty);
                    if (getPropertyResult.IsSuccess)
                    {
                        var jsonValue = getPropertyResult.Value;
                        var jsonNode = JsonNode.Parse(jsonValue);
                        if (jsonNode is null)
                        {
                            _logger.LogError($"Failed to parse JSON property: '{DocumentConstants.EditorModeProperty}'");
                            return;
                        }

                        var modeString = jsonNode.ToString();

                        if (!Enum.TryParse(modeString, out editorMode))
                        {
                            editorMode = EditorMode.Editor;
                        }
                    }
                }
            }

            ShowEditor = (editorMode == EditorMode.Editor || editorMode == EditorMode.EditorAndPreview);
            ShowPreview = (editorMode == EditorMode.Preview || editorMode == EditorMode.EditorAndPreview);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
        }
    }
}
