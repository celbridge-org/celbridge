using Celbridge.Commands;
using Celbridge.Console;
using Celbridge.ContextMenu;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Menu option to run a file in an open console. The console runner registry decides which open consoles
/// can run the clicked file. A single runner runs on click, several expand to a submenu.
/// </summary>
public class RunMenuOption : IMenuOption<ExplorerMenuContext>, ISubMenuOption<ExplorerMenuContext>
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 1;
    public string GroupId => nameof(ExplorerMenuGroup.DocumentActions);

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
        var hasRunner = GetRunTargets(context).Count > 0;
        return new MenuItemState(IsVisible: hasRunner, IsEnabled: hasRunner);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (!TryGetClickedResourceKey(context, out var scriptResource))
        {
            return;
        }

        var targets = GetRunTargets(context);
        if (targets.Count == 0)
        {
            return;
        }

        RunInConsole(targets[0].SessionId, scriptResource);
    }

    public IReadOnlyList<SubMenuItem> GetSubMenuItems(ExplorerMenuContext context)
    {
        if (!TryGetClickedResourceKey(context, out var scriptResource))
        {
            return Array.Empty<SubMenuItem>();
        }

        var targets = GetRunTargets(context);
        if (targets.Count <= 1)
        {
            return Array.Empty<SubMenuItem>();
        }

        var items = new List<SubMenuItem>();
        foreach (var target in targets)
        {
            var sessionId = target.SessionId;
            items.Add(new SubMenuItem(target.DisplayName, null, () => RunInConsole(sessionId, scriptResource)));
        }

        return items;
    }

    private void RunInConsole(Guid sessionId, ResourceKey scriptResource)
    {
        _commandService.Execute<IRunCommand>(command =>
        {
            command.ScriptResource = scriptResource;
            command.SessionId = sessionId;
        });
    }

    private IReadOnlyList<ConsoleRunTarget> GetRunTargets(ExplorerMenuContext context)
    {
        if (!TryGetClickedResourceKey(context, out var resourceKey))
        {
            return Array.Empty<ConsoleRunTarget>();
        }

        var extension = Path.GetExtension(resourceKey);
        var sessions = _workspaceWrapper.WorkspaceService.ConsoleService.Sessions;
        return sessions.GetRunTargets(extension);
    }

    private bool TryGetClickedResourceKey(ExplorerMenuContext context, out ResourceKey resourceKey)
    {
        resourceKey = ResourceKey.Empty;

        if (context.ClickedResource is not IFileResource clickedFile)
        {
            return false;
        }

        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return false;
        }

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        resourceKey = resourceRegistry.GetResourceKey(clickedFile);
        return true;
    }
}
