namespace Celbridge.Workshop;

/// <summary>
/// Maintains the web view document that presents the Workshop. The document is generated from the section
/// catalog rather than authored by the user.
/// </summary>
public interface IWorkshopService
{
    /// <summary>
    /// The resource key of the web view document that presents the Workshop.
    /// </summary>
    ResourceKey DocumentResource { get; }

    /// <summary>
    /// Writes the Workshop document. Called on workspace load. A failure is logged, not propagated.
    /// </summary>
    Task SeedDocumentAsync();

    /// <summary>
    /// Writes the canonical Workshop document, replacing whatever the file held.
    /// </summary>
    Task<Result> WriteDocumentAsync();
}
