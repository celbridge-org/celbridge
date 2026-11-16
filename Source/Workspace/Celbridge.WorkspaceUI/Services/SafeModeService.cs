using Celbridge.Documents;
using Celbridge.Packages;
using Celbridge.Projects;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Keeps the Safe Mode state of the workspace. The state comes from the current project when the workspace starts.
/// </summary>
public class SafeModeService : ISafeModeService
{
    // The editors and Utility Rail buttons that come with Celbridge and run no code from the project. Editor ids and
    // Utility Rail ids share one key space, so the Project Settings editor and its rail button have the same id.
    private static readonly IReadOnlySet<EditorId> AllowedEditorIds = new HashSet<EditorId>
    {
        BuiltInEditors.CodeEditorId,
        BuiltInEditors.ReportViewerId,
        BuiltInEditors.ProjectSettingsEditorId,
        BuiltInShortcutIds.ProjectSettings,
        BuiltInUtilityIds.Explorer,
        BuiltInUtilityIds.Search,
    };

    private readonly List<SafeModeCause> _causes = new();

    public bool IsActive => _causes.Count > 0;

    public IReadOnlyList<SafeModeCause> Causes => _causes;

    public SafeModeService(IProjectService projectService)
    {
        var project = projectService.CurrentProject;
        Guard.IsNotNull(project);

        if (!project.IsTrusted)
        {
            _causes.Add(SafeModeCause.Untrusted);
        }

        if (!project.ConfigIsHealthy)
        {
            _causes.Add(SafeModeCause.ConfigurationFailed);
        }
    }

    public bool IsAllowed(EditorId editorId)
    {
        if (!IsActive)
        {
            return true;
        }

        return AllowedEditorIds.Contains(editorId);
    }
}
