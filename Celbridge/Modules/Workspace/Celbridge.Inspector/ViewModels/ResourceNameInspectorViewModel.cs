using Celbridge.Explorer;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

using Path = System.IO.Path;

namespace Celbridge.Inspector.ViewModels;

public partial class ResourceNameInspectorViewModel : ObservableObject
{
    private readonly IIconService _iconService;
    private readonly IResourceRegistry _resourceRegistry;

    [ObservableProperty]
    private ResourceKey _resource;

    [ObservableProperty]
    private IconDefinition _icon;

    // Code gen requires a parameterless constructor
    public ResourceNameInspectorViewModel()
    {
        throw new NotImplementedException();
    }

    public ResourceNameInspectorViewModel(
        IIconService iconService,
        IWorkspaceWrapper workspaceWrapper)
    {
        // workspaceWrapper.IsWorkspaceLoaded could be false here if this is called while loading workspace.
        Guard.IsNotNull(workspaceWrapper.WorkspaceService);

        _iconService = iconService;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        _icon = _iconService.DefaultFileIcon;

        PropertyChanged += ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Resource))
        {
            // If the resource is a folder, use the folder icon
            var getResourceResult = _resourceRegistry.GetResource(Resource);
            if (getResourceResult.IsSuccess)
            {
                var resource = getResourceResult.Value;
                if (resource is IFolderResource) 
                {
                    Icon = _iconService.DefaultFolderIcon with
                    {
                        // Todo: Define this color in resources
                        FontColor = "#FFCC40"
                    };
                    return;
                }
            }

            // If the resource is a file, use the icon matching the file extension
            var fileExtension = Path.GetExtension(Resource);
            var getIconResult = _iconService.GetIconForFileExtension(fileExtension);            
            if (getIconResult.IsSuccess)
            {
                Icon = getIconResult.Value;
            }
        }
    }
}
