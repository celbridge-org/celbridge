namespace Celbridge.Packages;

/// <summary>
/// Represents a discovered package, containing its identity information
/// and all document editor contributions it provides.
/// </summary>
public record Package
{
    /// <summary>
    /// Package identity, permissions, and hosting information.
    /// </summary>
    public PackageInfo Info { get; init; } = new();

    /// <summary>
    /// Document editor contributions provided by this package.
    /// </summary>
    public IReadOnlyList<DocumentEditorContribution> DocumentEditors { get; init; } = [];
}
