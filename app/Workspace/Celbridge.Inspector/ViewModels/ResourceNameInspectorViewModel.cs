using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.Inspector.ViewModels;

public partial class ResourceNameInspectorViewModel : InspectorViewModel
{
    private readonly ICommandService _commandService;
    private readonly IResourceRegistry _resourceRegistry;

    /// <summary>
    /// Returns the file extension for the current resource, used by the FileIcon control.
    /// Returns "_folder" for folder resources to display the folder icon.
    /// </summary>
    public string FileExtension
    {
        get
        {
            if (Resource.IsEmpty)
            {
                return string.Empty;
            }

            // Check if resource is a folder
            var getResult = _resourceRegistry.GetResource(Resource);
            if (getResult.IsSuccess && getResult.Value is IFolderResource)
            {
                return "_folder";
            }

            return Path.GetExtension(Resource.ResourceName);
        }
    }

    // Code gen requires a parameterless constructor
    public ResourceNameInspectorViewModel()
    {
        throw new NotImplementedException();
    }

    public ResourceNameInspectorViewModel(
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        // workspaceWrapper.IsWorkspaceLoaded could be false here if this is called while loading workspace.
        Guard.IsNotNull(workspaceWrapper.WorkspaceService);

        _commandService = commandService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Resource))
        {
            OnPropertyChanged(nameof(FileExtension));
        }
    }

    public ICommand NavigateToResourceCommand => new RelayCommand(NavigateToResourceCommand_Execute);
    private void NavigateToResourceCommand_Execute()
    {
        _commandService.Execute<ISelectResourceCommand>(command => {
            command.Resource = Resource; 
        });
    }
}
