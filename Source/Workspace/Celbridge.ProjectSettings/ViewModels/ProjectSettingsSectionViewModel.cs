using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Documents;
using Celbridge.Explorer;
using Celbridge.Projects;
using Celbridge.Projects.Services;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// The state and services the Project Settings sections share, supplied by the editor that owns them.
/// </summary>
public sealed class ProjectSettingsContext
{
    private readonly Action _notifyEdited;

    public ProjectSettingsContext(
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        ICommandService commandService,
        Action notifyEdited)
    {
        WorkspaceWrapper = workspaceWrapper;
        ProjectService = projectService;
        CommandService = commandService;
        _notifyEdited = notifyEdited;
    }

    public IWorkspaceWrapper WorkspaceWrapper { get; }

    public IProjectService ProjectService { get; }

    public ICommandService CommandService { get; }

    /// <summary>
    /// The working copy the sections edit, replaced by the editor each time it loads the project file.
    /// Null until the first load.
    /// </summary>
    public ProjectConfigDraft? Draft { get; set; }

    public void NotifyEdited() => _notifyEdited();

    // The reconciled config (overrides only), falling back to the parsed config before reconcile. The
    // instance changes only when a discovery pass runs, so its identity signals whether a reload is needed.
    public ProjectConfig? GetConfig()
    {
        var packageService = WorkspaceWrapper.WorkspaceService?.PackageService;
        return packageService?.GetNormalizedConfig() ?? ProjectService.CurrentProject?.Config;
    }
}

/// <summary>
/// Base for the Project Settings section view models. Each section reads the reconciled config on Load
/// and mutates the shared draft as the user edits; the draft reaches disk on the editor's save tick, and
/// the running workspace only reflects it after a reload.
/// </summary>
public abstract class ProjectSettingsSectionViewModel : ObservableObject
{
    private readonly ProjectSettingsContext _context;

    protected ProjectSettingsSectionViewModel(ProjectSettingsContext context)
    {
        _context = context;
    }

    protected IWorkspaceService? WorkspaceService => _context.WorkspaceWrapper.WorkspaceService;

    protected IProjectService ProjectService => _context.ProjectService;

    protected ICommandService CommandService => _context.CommandService;

    protected ProjectConfig? GetConfig() => _context.GetConfig();

    /// <summary>
    /// Mutates the draft and reports the edit. A no-op before the editor has loaded a draft.
    /// </summary>
    protected void EditConfig(Action<ProjectConfigDraft> edit)
    {
        var draft = _context.Draft;
        if (draft is null)
        {
            return;
        }

        edit(draft);

        _context.NotifyEdited();
    }

    // Opens a manifest as a document for editing.
    protected void OpenManifest(ResourceKey manifestResource)
    {
        CommandService.Execute<IOpenDocumentCommand>(command => command.FileResource = manifestResource);
    }

    // Reveals a manifest in the Explorer without opening it.
    protected void RevealManifest(ResourceKey manifestResource)
    {
        CommandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = manifestResource;
            command.ShowExplorerPanel = true;
        });
    }

    /// <summary>
    /// Rebuilds the section's state from the reconciled config.
    /// </summary>
    public abstract void Load();
}
