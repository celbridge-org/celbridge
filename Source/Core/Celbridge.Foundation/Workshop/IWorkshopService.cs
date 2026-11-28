namespace Celbridge.Workshop;

/// <summary>
/// Maintains the Workshop's browser document. The Workshop button on the Utility Rail opens the document. Celbridge
/// writes the document, and the user never edits it.
/// </summary>
public interface IWorkshopService
{
    /// <summary>
    /// The resource key of the Workshop's browser document.
    /// </summary>
    ResourceKey DocumentResource { get; }

    /// <summary>
    /// Writes the Workshop's document as a read-only file. Called on workspace load. A failure is logged, not
    /// propagated.
    /// </summary>
    Task SeedDocumentAsync();
}
