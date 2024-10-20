using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.ResourceData;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.Inspector.ViewModels;

public partial class MarkdownInspectorViewModel : InspectorViewModel
{
    private readonly ILogger<MarkdownInspectorViewModel> _logger;
    private readonly ICommandService _commandService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IResourceDataService _resourceDataService;

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
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _commandService = commandService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
        _resourceDataService = workspaceWrapper.WorkspaceService.ResourceDataService;

        PropertyChanged += MarkdownInspectorViewModel_PropertyChanged;
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
            _resourceDataService.SetProperty(Resource, ResourceDataConstants.TextEditor_ShowEditor, showEditor);
            _resourceDataService.SetProperty(Resource, ResourceDataConstants.TextEditor_ShowPreview, showPreview);

            UpdateButtonState();

            // Todo: Save the resource data on a timer
            _resourceDataService.SavePendingAsync();
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
            bool showingEditor = _resourceDataService.GetProperty(Resource, ResourceDataConstants.TextEditor_ShowEditor, true);
            bool showingPreview = _resourceDataService.GetProperty(Resource, ResourceDataConstants.TextEditor_ShowPreview, true);
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
