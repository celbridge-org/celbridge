using Celbridge.Documents;
using Microsoft.Extensions.Localization;

namespace Celbridge.Resources;

/// <summary>
/// Factory that claims ownership of Celbridge project files via the .celbridge extension. Project files are
/// never opened as in-workspace documents.
/// </summary>
public class ProjectFileFactory : DocumentEditorFactoryBase
{
    private readonly IStringLocalizer _stringLocalizer;

    public override EditorInstanceId EditorId { get; } = new("celbridge.project-file");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_ProjectFile");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".celbridge"];

    public override bool IsPlaceholder => true;

    public ProjectFileFactory(IStringLocalizer stringLocalizer)
    {
        _stringLocalizer = stringLocalizer;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        return Result<IDocumentView>.Fail(
            $"Project file '{fileResource}' is not opened as a document; it is loaded by the project service.");
    }
}
