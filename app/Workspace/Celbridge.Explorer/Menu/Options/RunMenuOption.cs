using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.ContextMenu;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to run an executable script (Python).
/// </summary>
public class RunMenuOption : IMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 1;
    public string GroupId => ExplorerMenuGroups.DocumentActions;

    public RunMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        return new MenuItemDisplayInfo(_stringLocalizer.GetString("ResourceTree_Run"));
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        if (!context.HasSingleSelection)
        {
            return new MenuItemState(IsVisible: false, IsEnabled: false);
        }

        var isExecutable = IsResourceExecutable(context.SingleSelectedResource);
        return new MenuItemState(IsVisible: true, IsEnabled: isExecutable);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (context.SingleSelectedResource is not IFileResource fileResource)
        {
            return;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resourceKey = resourceRegistry.GetResourceKey(fileResource);
        var extension = Path.GetExtension(resourceKey);

        if (extension != ExplorerConstants.PythonExtension &&
            extension != ExplorerConstants.IPythonExtension)
        {
            return;
        }

        _commandService.Execute<IRunCommand>(command =>
        {
            command.ScriptResource = resourceKey;
        });
    }

    private bool IsResourceExecutable(IResource? resource)
    {
        if (resource is not IFileResource fileResource)
        {
            return false;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var pythonService = _workspaceWrapper.WorkspaceService.PythonService;
        var resourceKey = resourceRegistry.GetResourceKey(fileResource);
        var extension = Path.GetExtension(resourceKey);

        if (extension == ExplorerConstants.PythonExtension ||
            extension == ExplorerConstants.IPythonExtension)
        {
            return pythonService.IsPythonHostAvailable;
        }

        return false;
    }
}
