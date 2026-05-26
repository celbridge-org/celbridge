using Celbridge.Documents;
using Microsoft.Extensions.Localization;

namespace Celbridge.Packages;

/// <summary>
/// Factory that claims ownership of per-contribution document manifests
/// (*.document.cel). These files are sub-components of a package, loaded by
/// PackageManifestLoader as part of package.cel resolution; they are never opened
/// as in-workspace documents. The factory reserves the extension so the
/// resources subsystem classifies a .document.cel file as a standalone .cel
/// form rather than an orphan .cel file.
/// </summary>
public class DocumentContributionFactory : DocumentEditorFactoryBase
{
    private readonly IStringLocalizer _stringLocalizer;

    public override DocumentEditorId EditorId { get; } = new("celbridge.document-contribution");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_DocumentContribution");

    public override IReadOnlyList<string> SupportedExtensions { get; } = [".document.cel"];

    public override bool IsPlaceholder => true;

    public DocumentContributionFactory(IStringLocalizer stringLocalizer)
    {
        _stringLocalizer = stringLocalizer;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        // Document contributions are loaded by PackageManifestLoader as part of
        // a parent package.cel; opening one as a document is not a supported flow.
        return Result<IDocumentView>.Fail(
            $"Document contribution '{fileResource}' is not opened as a document; it is loaded by the package service.");
    }
}
