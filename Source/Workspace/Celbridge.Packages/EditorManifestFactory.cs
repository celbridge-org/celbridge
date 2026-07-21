using Celbridge.Documents;
using Microsoft.Extensions.Localization;

namespace Celbridge.Packages;

/// <summary>
/// Factory that claims ownership of per-contribution editor manifests (*.editor.toml). These files are
/// sub-components of a package, loaded by PackageManifestLoader as part of package.toml resolution. They are
/// never opened as in-workspace documents.
/// </summary>
public class EditorManifestFactory : DocumentEditorFactoryBase
{
    private readonly IStringLocalizer _stringLocalizer;

    public override EditorId EditorId { get; } = new("celbridge.editor-manifest");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_EditorManifest");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [PackageManifestLoader.EditorManifestExtension];

    public override bool IsPlaceholder => true;

    public EditorManifestFactory(IStringLocalizer stringLocalizer)
    {
        _stringLocalizer = stringLocalizer;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        return Result<IDocumentView>.Fail(
            $"Editor manifest '{fileResource}' is not opened as a document; it is loaded by the package service.");
    }
}
