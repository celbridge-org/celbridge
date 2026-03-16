namespace Celbridge.Extensions;

/// <summary>
/// Represents a discovered extension, containing its identity information
/// and all document editor contributions it provides.
/// </summary>
public record Extension
{
    /// <summary>
    /// FileExtension identity, permissions, and hosting information.
    /// </summary>
    public ExtensionInfo Info { get; init; } = new();

    /// <summary>
    /// Document editor contributions provided by this extension.
    /// </summary>
    public IReadOnlyList<DocumentContribution> DocumentEditors { get; init; } = [];
}
