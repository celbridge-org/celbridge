using Celbridge.Commands;
using Celbridge.ContextMenu;
using Celbridge.Documents;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Menu.Options;

/// <summary>
/// Optional power-user menu option that opens a resource's .cel metadata
/// sidecar in the Code editor. Only visible when the project config has set
/// the [features].open-cel flag to true and the clicked resource has a
/// non-null sidecar link.
/// </summary>
public class OpenCelMenuOption : IMenuOption<ExplorerMenuContext>
{
    private const string OpenCelFeatureFlag = "open-cel";

    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IProjectService _projectService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public int Priority => 4;
    public string GroupId => nameof(ExplorerMenuGroup.DocumentActions);

    public OpenCelMenuOption(
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IProjectService projectService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _projectService = projectService;
        _workspaceWrapper = workspaceWrapper;
    }

    public MenuItemDisplayInfo GetDisplayInfo(ExplorerMenuContext context)
    {
        var label = _stringLocalizer.GetString("Explorer_OpenCel");
        return new MenuItemDisplayInfo(label);
    }

    public MenuItemState GetState(ExplorerMenuContext context)
    {
        if (!IsOpenCelFeatureEnabled())
        {
            return new MenuItemState(IsVisible: false, IsEnabled: false);
        }

        if (context.ClickedResource is not IFileResource clickedFile)
        {
            return new MenuItemState(IsVisible: false, IsEnabled: false);
        }

        // The option only applies to plain files that already have a sidecar
        // on disk. Showing it for sidecar-less files would invite an empty-
        // sidecar create through a power-user surface, which is not what this
        // affordance is for.
        bool hasSidecar = clickedFile.Sidecar is not null;
        return new MenuItemState(IsVisible: hasSidecar, IsEnabled: hasSidecar);
    }

    public void Execute(ExplorerMenuContext context)
    {
        if (context.ClickedResource is not IFileResource clickedFile)
        {
            return;
        }

        var sidecarLink = clickedFile.Sidecar;
        if (sidecarLink is null)
        {
            return;
        }

        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = sidecarLink.Key;
            command.EditorId = DocumentConstants.CodeEditorId;
        });
    }

    private bool IsOpenCelFeatureEnabled()
    {
        var project = _projectService.CurrentProject;
        if (project is null)
        {
            return false;
        }

        return project.Config.Features.TryGetValue(OpenCelFeatureFlag, out var enabled)
            && enabled;
    }
}
