namespace Celbridge.Packages;

/// <summary>
/// Represents a discovered package, containing its identity information
/// and all editor contributions it provides.
/// </summary>
public record Package
{
    /// <summary>
    /// Package identity, permissions, and hosting information.
    /// </summary>
    public PackageInfo Info { get; init; } = new();

    /// <summary>
    /// Editor contributions provided by this package.
    /// </summary>
    public IReadOnlyList<EditorContribution> Editors { get; init; } = [];

    /// <summary>
    /// Fields the package manifest declared that the host does not define, each named by its section (for
    /// example "package.author"). Fields declared by the editor manifests are carried on each contribution.
    /// </summary>
    public IReadOnlyList<string> UnknownFields { get; init; } = [];
}
