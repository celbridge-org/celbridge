using Celbridge.Documents.Services;
using Celbridge.Entities;
using Celbridge.Inspector;
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
    private readonly IInspectorService _inspectorService;

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
        _inspectorService = workspaceWrapper.WorkspaceService.InspectorService;

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
            // Check for a root component that has the "isPreviewController" attribute set to "true" and that has
            // an "/editorMode" property. The attribute here performs roughly the same function as an interface in
            // a language like C#.
            var editorMode = EditorMode.Editor;
            var componentKey = new ComponentKey(_fileResource, 0);

            // Get the "/editorMode" property via the root component editor.
            // We query the component editor here rather than the component itself. This allows the component editor
            // class to intercept the property request if needed.
            var getEditorResult = _inspectorService.AcquireComponentEditor(componentKey);

            if (getEditorResult.IsSuccess)
            {
                var editor = getEditorResult.Value;

                var isPreviewController = editor.Component.SchemaReader.GetBooleanAttribute("isPreviewController");
                if (isPreviewController)
                {
                    var getPropertyResult = editor.GetProperty(DocumentConstants.EditorModeProperty);
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
