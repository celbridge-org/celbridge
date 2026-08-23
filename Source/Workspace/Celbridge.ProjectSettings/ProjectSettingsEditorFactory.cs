using Celbridge.Documents;
using Celbridge.Packages;
using Celbridge.Projects;
using Celbridge.ProjectSettings.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings;

/// <summary>
/// Creates the Project Settings editor for the loaded project's .celbridge file. A folder may hold
/// several configurations; only the loaded one has a running reconciliation behind it, so the others
/// fall through to the Code Editor and are hand-edited as TOML.
/// </summary>
public class ProjectSettingsEditorFactory : DocumentEditorFactoryBase
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectService _projectService;

    public override EditorId EditorId { get; } = BuiltInEditors.ProjectSettingsEditorId;

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_ProjectSettings");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [ProjectConstants.ProjectFileExtension];

    public override bool ReservesFileType => true;

    public ProjectSettingsEditorFactory(
        IStringLocalizer stringLocalizer,
        IProjectService projectService)
    {
        _stringLocalizer = stringLocalizer;
        _projectService = projectService;
    }

    public override bool CanHandleResource(ResourceKey fileResource)
    {
        var project = _projectService.CurrentProject;

        return project is not null
            && project.IsProjectFile(fileResource);
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        var documentView = new ProjectSettingsEditorView
        {
            EditorId = EditorId
        };

        return documentView.OkResult<IDocumentView>();
    }
}
