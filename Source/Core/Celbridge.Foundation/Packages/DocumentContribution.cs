using Celbridge.Documents;

namespace Celbridge.Packages;

/// <summary>
/// Base class for document editor contributions parsed from a TOML document manifest.
/// Each extension can contribute one or more document editors via its extension.toml.
/// Subclasses define the specific editor type and its configuration.
/// </summary>
public abstract partial record DocumentContribution
{
    /// <summary>
    /// The parent package that provides this contribution.
    /// </summary>
    public PackageInfo Package { get; init; } = new();

    /// <summary>
    /// Unique identifier for this document contribution (e.g., "note-document").
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The document file types this editor handles. Each entry declares the file extension and
    /// an optional display name or localization key for the Add File dialog.
    /// </summary>
    public IReadOnlyList<DocumentFileType> FileTypes { get; init; } = [];

    /// <summary>
    /// Priority for conflict resolution when multiple editors support the same extension.
    /// </summary>
    public EditorPriority Priority { get; init; }

    /// <summary>
    /// Optional list of document templates provided by this extension.
    /// </summary>
    public IReadOnlyList<DocumentTemplate> Templates { get; init; } = [];
}
