namespace Celbridge.Community;

/// <summary>
/// Maintains the web view document that presents the community resources. The document is generated rather
/// than authored by the user.
/// </summary>
public interface ICommunityService
{
    /// <summary>
    /// The resource key of the web view document that presents the community resources.
    /// </summary>
    ResourceKey DocumentResource { get; }

    /// <summary>
    /// Writes the Community document. Called on workspace load. A failure is logged, not propagated.
    /// </summary>
    Task SeedDocumentAsync();
}
