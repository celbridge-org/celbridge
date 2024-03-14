using Celbridge.BaseLibrary.UserInterface;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Workspace.Services;

public class WorkspaceService : IWorkspaceService
{
    public IServiceProvider _serviceProvider;
    public IUserInterfaceService _userInterfaceService;

    public WorkspaceService(IServiceProvider serviceProvider,
        IUserInterfaceService userInterfaceService)
    {
        _serviceProvider = serviceProvider;
        _userInterfaceService = userInterfaceService;
    }

    /// <summary>
    /// Instantiate the workspace panels that are registered with the UserInterfaceService.
    /// </summary>
    public Dictionary<WorkspacePanelType, UIElement> CreateWorkspacePanels()
    {
        var panels = new Dictionary<WorkspacePanelType, UIElement>();
        foreach (var config in _userInterfaceService.WorkspacePanelConfigs)
        {
            if (panels.ContainsKey(config.PanelType))
            {
                throw new InvalidOperationException($"Panel type '{config.PanelType}' is already registered.");
            }   

            // Instantiate the panel
            var panel = _serviceProvider.GetRequiredService(config.ViewType) as UIElement;
            if (panel is null)
            {
                throw new Exception($"Failed to create a workspace panel of type '{config.ViewType}'");
            }

            panels.Add(config.PanelType, panel);
        }

        return panels;
    }
}
