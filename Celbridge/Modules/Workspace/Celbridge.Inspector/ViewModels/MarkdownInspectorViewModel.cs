using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Entities;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.Inspector.ViewModels;

public partial class MarkdownInspectorViewModel : InspectorViewModel
{
    private readonly ILogger<MarkdownInspectorViewModel> _logger;
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IEntityService _entityService;

    [ObservableProperty]
    private bool _showEditorEnabled;

    [ObservableProperty]
    private bool _showBothEnabled;

    [ObservableProperty]
    private bool _showPreviewEnabled;

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

        _messengerService.Register<EntityChangedMessage>(this, OnEntityChangedMessage);

        PropertyChanged += MarkdownInspectorViewModel_PropertyChanged;
    }

    private void OnEntityChangedMessage(object recipient, EntityChangedMessage message)
    {
        if (message.Resource == Resource)            
        {
            if (message.PropertyPaths.Contains(TextEditorEntityConstants.ShowEditor) ||
                message.PropertyPaths.Contains(TextEditorEntityConstants.ShowPreview))
            {
                UpdateButtonState();
            }
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
        SetPanelVisibility(true, false);
    }

    public IRelayCommand ShowPreviewCommand => new RelayCommand(ShowPreview_Executed);
    private void ShowPreview_Executed()
    {
        SetPanelVisibility(false, true);
    }

    public IRelayCommand ShowBothCommand => new RelayCommand(ShowEditorAndPreview_Executed);
    private void ShowEditorAndPreview_Executed()
    {
        SetPanelVisibility(true, true);
    }

    private void SetPanelVisibility(bool showEditor, bool showPreview)
    {
        Guard.IsTrue(showEditor || showPreview);

        try
        {
            _entityService.SetProperty(Resource, TextEditorEntityConstants.ShowEditor, showEditor);
            _entityService.SetProperty(Resource, TextEditorEntityConstants.ShowPreview, showPreview);

            UpdateButtonState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
        }
    }

    private void UpdateButtonState()
    {
        try
        {
            bool showingEditor = _entityService.GetProperty(Resource, TextEditorEntityConstants.ShowEditor, true);
            bool showingPreview = _entityService.GetProperty(Resource, TextEditorEntityConstants.ShowPreview, true);
            bool showingBoth = showingEditor && showingPreview;

            ShowEditorEnabled = !showingEditor || (showingBoth);
            ShowPreviewEnabled = !showingPreview || (showingBoth);
            ShowBothEnabled = !showingBoth;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
        }
    }
}
