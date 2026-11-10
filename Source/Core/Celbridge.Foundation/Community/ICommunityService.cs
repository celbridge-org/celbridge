namespace Celbridge.Community;

/// <summary>
/// Maintains the browser document that presents the community resources. The document is generated rather
/// than authored by the user.
/// </summary>
public interface ICommunityService
{
    /// <summary>
    /// The resource key of the browser document that presents the community resources.
    /// </summary>
    ResourceKey DocumentResource { get; }

    /// <summary>
    /// Writes the Community document as a read-only file. Called on workspace load. A failure is logged, not
    /// propagated.
    /// </summary>
    Task SeedDocumentAsync();
}
