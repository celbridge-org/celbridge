using Celbridge.Documents;
using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.Inspector.ViewModels;

public partial class ResourceNameInspectorViewModel : InspectorViewModel
{
    private readonly ILogger<ResourceNameInspectorViewModel> _logger;
    private readonly ICommandService _commandService;
    private readonly IExplorerService _explorerService;

    [ObservableProperty]
    private IconDefinition _icon;

    // Code gen requires a parameterless constructor
    public ResourceNameInspectorViewModel()
    {
        throw new NotImplementedException();
    }

    public ResourceNameInspectorViewModel(
        ILogger<ResourceNameInspectorViewModel> logger,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        // workspaceWrapper.IsWorkspaceLoaded could be false here if this is called while loading workspace.
        Guard.IsNotNull(workspaceWrapper.WorkspaceService);

        _logger = logger;
        _commandService = commandService;
        _explorerService = workspaceWrapper.WorkspaceService.ExplorerService;

        // Use the default file icon until we can resolve the proper icon when the resource is populated.
        _icon = _explorerService.GetIconForResource(ResourceKey.Empty);

        PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Resource))
        {
            Icon = _explorerService.GetIconForResource(Resource);
        }
    }

    public ICommand NavigateToResourceCommand => new RelayCommand(NavigateToResourceCommand_Execute);
    private void NavigateToResourceCommand_Execute()
    {
        _commandService.Execute<ISelectResourceCommand>(command => {
            command.Resource = Resource; 
        });
    }

    public IRelayCommand OpenDocumentCommand => new RelayCommand(OpenDocument_Executed);
    private void OpenDocument_Executed()
    {
        // Execute a command to open the web document. Force the document to reload if it is already open.
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = Resource;
            command.ForceReload = true;
        });
    }

    public IRelayCommand DeleteDocumentCommand => new RelayCommand(DeleteDocument_Executed);
    private void DeleteDocument_Executed()
    {
        // Execute a command to show the delete resource dialog
        _commandService.Execute<IDeleteResourceDialogCommand>(command =>
        {
            command.Resource = Resource;
        });
    }

    public IRelayCommand OpenResourceInExplorerCommand => new RelayCommand(OpenResourceInExplorer_Executed);
    private void OpenResourceInExplorer_Executed()
    {
        // Execute a command to open the resource in the system file manager
        _commandService.Execute<IOpenFileManagerCommand>(command =>
        {
            command.Resource = Resource;
        });
    }

    public IRelayCommand OpenResourceInApplicationCommand => new RelayCommand(OpenResourceInApplication_Executed);
    public void OpenResourceInApplication_Executed()
    {
        // This will execute a command to open the resource in an application.
        _explorerService.OpenResource(Resource);
    }
}
