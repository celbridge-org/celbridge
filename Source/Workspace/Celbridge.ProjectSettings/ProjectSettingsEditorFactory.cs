using Celbridge.Documents;
using Celbridge.ProjectSettings.Views;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings;

/// <summary>
/// Creates the Project Settings editor for the project's .celbridge file. There is one such file per
/// project and it is hidden from the resource tree, so this editor is reached from the Project Settings
/// rail button rather than by opening a file.
/// </summary>
public class ProjectSettingsEditorFactory : DocumentEditorFactoryBase
{
    private readonly IStringLocalizer _stringLocalizer;

    public override EditorId EditorId { get; } = new("celbridge.project-file");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_ProjectFile");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".celbridge"];

    public ProjectSettingsEditorFactory(IStringLocalizer stringLocalizer)
    {
        _stringLocalizer = stringLocalizer;
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
