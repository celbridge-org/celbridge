using Celbridge.Documents;
using Microsoft.Extensions.Localization;

namespace Celbridge.Resources;

/// <summary>
/// Factory that claims ownership of Celbridge project files via the .celbridge
/// extension. Registering through the standard factory surface consolidates
/// project-file identity in the same registry that other document editors use.
/// </summary>
public class ProjectFileFactory : DocumentEditorFactoryBase
{
    private readonly IStringLocalizer _stringLocalizer;

    public override DocumentEditorId EditorId { get; } = new("celbridge.project-file");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_ProjectFile");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".celbridge"];

    public override bool IsPlaceholder => true;

    public ProjectFileFactory(IStringLocalizer stringLocalizer)
    {
        _stringLocalizer = stringLocalizer;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        // The project file is loaded by ProjectService.LoadProjectAsync, not as
        // an in-workspace document view. Registering here reserves ownership of
        // the extension; opening one as a document is not a supported flow.
        return Result<IDocumentView>.Fail(
            $"Project file '{fileResource}' is not opened as a document; it is loaded by the project service.");
    }
}
