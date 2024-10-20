using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.ResourceData;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.Inspector.ViewModels;

public partial class MarkdownInspectorViewModel : InspectorViewModel
{
    private const string ShowPreviewKey = "ShowPreview";
    private const string ShowEditorKey = "ShowEditor";

    private readonly ILogger<MarkdownInspectorViewModel> _logger;
    private readonly ICommandService _commandService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IResourceDataService _resourceDataService;

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

    public IRelayCommand ToggleEditorCommand => new RelayCommand(ToggleEditor_Executed);
    private void ToggleEditor_Executed()
    {
        try
        {
            bool showEditor = _resourceDataService.GetProperty(Resource, ShowEditorKey, false);
            showEditor = !showEditor;
            _resourceDataService.SetProperty(Resource, ShowEditorKey, showEditor);

            // Todo: Save the resource data on a timer
            _resourceDataService.SavePendingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
        }
    }

    public IRelayCommand TogglePreviewCommand => new RelayCommand(TogglePreview_Executed);
    private void TogglePreview_Executed()
    {
        try
        {
            bool showPreview = _resourceDataService.GetProperty(Resource, ShowPreviewKey, false);
            showPreview = !showPreview;
            _resourceDataService.SetProperty(Resource, ShowPreviewKey, showPreview);

            // Todo: Save the resource data on a timer
            _resourceDataService.SavePendingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
        }
    }
}
