using Celbridge.Documents;
using Microsoft.Extensions.Localization;

namespace Celbridge.Packages;

/// <summary>
/// Factory that claims ownership of mod manifest files. Currently matches the
/// legacy package.toml filename; the next migration phase switches to the
/// .mod.cel multi-part extension. Registering through the standard factory
/// surface consolidates mod-manifest identity in the same registry that other
/// document editors use.
/// </summary>
public class ModManifestFactory : DocumentEditorFactoryBase
{
    private const string PackageTomlFilename = "package.toml";

    private readonly IStringLocalizer _stringLocalizer;

    public override DocumentEditorId EditorId { get; } = new("celbridge.mod-manifest");

    public override string DisplayName => _stringLocalizer.GetString("DocumentEditor_ModManifest");

    public override IReadOnlyList<string> SupportedExtensions { get; } = Array.Empty<string>();

    public override IReadOnlyList<string> SupportedFilenames { get; } = [PackageTomlFilename];

    public ModManifestFactory(IStringLocalizer stringLocalizer)
    {
        _stringLocalizer = stringLocalizer;
    }

    public override Result<IDocumentView> CreateDocumentView(ResourceKey fileResource)
    {
        // The mod manifest is loaded by PackageManifestLoader.LoadPackage, not
        // as an in-workspace document view. Registering here reserves
        // ownership of the filename; opening one as a document is not a
        // supported flow.
        return Result<IDocumentView>.Fail(
            $"Mod manifest '{fileResource}' is not opened as a document; it is loaded by the package service.");
    }
}
