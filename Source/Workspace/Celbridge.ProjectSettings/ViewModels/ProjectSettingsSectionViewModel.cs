using Celbridge.Commands;
using Celbridge.Projects;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.ProjectSettings.ViewModels;

/// <summary>
/// The shared dependencies a Project Settings section view model needs from the panel: the workspace and
/// project services to read the reconciled config, the command service to queue config edits, and a
/// callback each section invokes after writing an edit.
/// </summary>
public sealed record ProjectSettingsContext(
    IWorkspaceWrapper WorkspaceWrapper,
    IProjectService ProjectService,
    ICommandService CommandService,
    Action NotifyEdited);

/// <summary>
/// Base for the three Project Settings section view models (Information, Packages, File Editors). Each
/// section reads the reconciled config on Load and writes its edits through the shared command pipeline;
/// the running workspace only reflects the edits after the panel's apply-and-reload gesture.
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

    // The reconciled config (overrides only), falling back to the parsed config before reconcile.
    protected ProjectConfig? GetConfig()
    {
        var packageService = WorkspaceService?.PackageService;
        return packageService?.GetNormalizedConfig() ?? ProjectService.CurrentProject?.Config;
    }

    protected void WriteEdits(params ProjectConfigEdit[] edits)
    {
        _context.CommandService.Execute<IWriteProjectConfigCommand>(command => command.Edits = edits);
        _context.NotifyEdited();
    }

    /// <summary>
    /// Rebuilds the section's state from the reconciled config.
    /// </summary>
    public abstract void Load();
}
