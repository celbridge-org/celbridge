using Celbridge.Documents;
using Microsoft.Extensions.Localization;

namespace Celbridge.Packages;

/// <summary>
/// Factory that claims ownership of package manifest files by exact filename
/// (package.cel). The manifest sits at the top of each package folder and has no
/// stem segment, so it is matched by filename rather than by a multi-part
/// extension form. Registering through the standard factory surface
/// consolidates package-manifest identity in the same registry that other
/// document editors use.
/// </summary>
public class PackageManifestFactory : DocumentEditorFactoryBase
{
    private const string PackageManifestFilename = "package.cel";

    private readonly IStringLocalizer _stringLocalizer;

    public override DocumentEditorId EditorId { get; } = new("celbridge.package-manifest");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_PackageManifest");

    public override IReadOnlyList<string> SupportedExtensions { get; } = Array.Empty<string>();

    public override IReadOnlyList<string> SupportedFilenames { get; } = [PackageManifestFilename];

    public override bool IsPlaceholder => true;

    public PackageManifestFactory(IStringLocalizer stringLocalizer)
    {
        _stringLocalizer = stringLocalizer;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        // The package manifest is loaded by PackageManifestLoader.LoadPackage, not
        // as an in-workspace document view. Registering here reserves
        // ownership of the extension; opening one as a document is not a
        // supported flow.
        return Result<IDocumentView>.Fail(
            $"Package manifest '{fileResource}' is not opened as a document; it is loaded by the package service.");
    }
}
