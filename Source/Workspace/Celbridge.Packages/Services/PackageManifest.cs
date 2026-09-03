using Tomlyn.Serialization;

namespace Celbridge.Packages;

/// <summary>
/// The [package] section of a package manifest, identifying the package.
/// </summary>
internal sealed partial record ManifestPackageSection
{
    // Matches the name the package is published under on the workshop. The "celbridge-" prefix is
    // reserved for packages bundled with the app.
    public string? Name { get; init; }

    // Localization key naming the product, shown in Project Settings.
    public string? Title { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// The [contributes] section of a package manifest, listing what the package adds to a workspace.
/// </summary>
internal sealed record ManifestContributesSection
{
    // Package-relative paths to the editor manifests the package provides, each named with a stem so
    // that "editor.toml" alone is not one.
    public List<string>? Editors { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// The [permissions] section of a package manifest, declaring what its editors may do at runtime.
/// </summary>
internal sealed record ManifestPermissionsSection
{
    // Host tools the package's editors may call, in alias form ("document.save"). A trailing wildcard
    // covers a namespace, and "*" covers every tool.
    public List<string>? Tools { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}

/// <summary>
/// The shape of a package manifest (package.toml), deserialized by Tomlyn. The property names are the
/// manifest's known keys, and every other key lands in an UnknownKeys bag rather than being dropped.
/// </summary>
internal sealed record PackageManifest
{
    public ManifestPackageSection? Package { get; init; }
    public ManifestContributesSection? Contributes { get; init; }
    public ManifestPermissionsSection? Permissions { get; init; }

    [TomlExtensionData]
    public Dictionary<string, object?> UnknownKeys { get; init; } = new();
}
