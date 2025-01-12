using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.Inspector.ViewModels;

public partial class MarkdownInspectorViewModel : InspectorViewModel
{
    private const string MarkdownComponent = "Markdown";

    private readonly ILogger<MarkdownInspectorViewModel> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IEntityService _entityService;

    [ObservableProperty]
    private EditorMode _editorMode;

    // Code gen requires a parameterless constructor
    public MarkdownInspectorViewModel()
    {
        throw new NotImplementedException();
    }

    public MarkdownInspectorViewModel(
        ILogger<MarkdownInspectorViewModel> logger,
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _commandService = commandService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
        _entityService = workspaceWrapper.WorkspaceService.EntityService;

        _messengerService.Register<ComponentChangedMessage>(this, OnComponentChangedMessage);

        PropertyChanged += MarkdownInspectorViewModel_PropertyChanged;
    }

    private void OnComponentChangedMessage(object recipient, ComponentChangedMessage message)
    {
        if (message.ComponentKey.Resource == Resource &&
            message.ComponentType == MarkdownComponent &&
            message.PropertyPath == MarkdownComponentConstants.EditorMode)
        {
            UpdateButtonState();
        }
    }

    private void MarkdownInspectorViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Resource))
        {
            UpdateButtonState();
        }
    }

    public IRelayCommand OpenDocumentCommand => new RelayCommand(OpenDocument_Executed);
    private void OpenDocument_Executed()
    {
        // Execute a command to open the markdown document.
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = Resource;
            command.ForceReload = false;
        });
    }

    public IRelayCommand ShowEditorCommand => new RelayCommand(ShowEditor_Executed);
    private void ShowEditor_Executed()
    {
        SetEditorMode(EditorMode.Editor);
    }

    public IRelayCommand ShowPreviewCommand => new RelayCommand(ShowPreview_Executed);
    private void ShowPreview_Executed()
    {
        SetEditorMode(EditorMode.Preview);
    }

    public IRelayCommand ShowBothCommand => new RelayCommand(ShowEditorAndPreview_Executed);
    private void ShowEditorAndPreview_Executed()
    {
        SetEditorMode(EditorMode.EditorAndPreview);
    }

    private void SetEditorMode(EditorMode editorMode)
    {
        // Get the component
        var getComponentResult = _entityService.GetComponentOfType(Resource, MarkdownComponent);
        if (getComponentResult.IsFailure)
        {
            _logger.LogError(getComponentResult.Error);
            return;
        }
        var component = getComponentResult.Value;

        // Set the property
        var setResult = component.SetProperty(MarkdownComponentConstants.EditorMode, editorMode);
        if (setResult.IsFailure)
        {
            _logger.LogError(setResult.Error);
        }
    }

    private void UpdateButtonState()
    {
        try
        {
            // Get the component
            var getComponentResult = _entityService.GetComponentOfType(Resource, MarkdownComponent);
            if (getComponentResult.IsFailure)
            {
                _logger.LogError(getComponentResult.Error);
                return;
            }
            var component = getComponentResult.Value;

            // Get the property
            EditorMode = component.GetProperty(MarkdownComponentConstants.EditorMode, EditorMode.Editor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
        }
    }
}
