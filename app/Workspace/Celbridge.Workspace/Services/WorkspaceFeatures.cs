using Celbridge.Projects;
using Celbridge.Settings;

namespace Celbridge.Workspace.Services;

/// <summary>
/// Checks workspace-level and application-level feature flags.
/// </summary>
public class WorkspaceFeatures : IWorkspaceFeatures
{
    private readonly IProjectService _projectService;
    private readonly IFeatureFlagService _featureFlagService;

    public WorkspaceFeatures(
        IProjectService projectService,
        IFeatureFlagService featureFlagService)
    {
        _projectService = projectService;
        _featureFlagService = featureFlagService;
    }

    public bool IsEnabled(string featureName)
    {
        var project = _projectService.CurrentProject;

        if (project != null &&
            project.Config.Features.TryGetValue(featureName, out var workspaceValue))
        {
            return workspaceValue;
        }

        return _featureFlagService.IsEnabled(featureName);
    }
}
