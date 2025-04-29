using Celbridge.Explorer;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Inspector.ViewModels;

public partial class ResourceNameInspectorViewModel : InspectorViewModel
{
    private readonly IExplorerService _explorerService;

    [ObservableProperty]
    private IconDefinition _icon;

    // Code gen requires a parameterless constructor
    public ResourceNameInspectorViewModel()
    {
        throw new NotImplementedException();
    }

    public ResourceNameInspectorViewModel(IWorkspaceWrapper workspaceWrapper)
    {
        // workspaceWrapper.IsWorkspaceLoaded could be false here if this is called while loading workspace.
        Guard.IsNotNull(workspaceWrapper.WorkspaceService);

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
}
