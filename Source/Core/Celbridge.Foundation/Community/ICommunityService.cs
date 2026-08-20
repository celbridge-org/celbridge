namespace Celbridge.Community;

/// <summary>
/// Maintains the web view documents that back the community links. Each document is regenerated from the
/// catalog rather than authored by the user, so a link always opens on a known-good page.
/// </summary>
public interface ICommunityService
{
    /// <summary>
    /// Writes the web view document for every community link. Called on workspace load, before documents
    /// are restored, because the temp: root that holds them is wiped on every load. Per-link failures are
    /// logged, not propagated.
    /// </summary>
    Task SeedLinkDocumentsAsync();

    /// <summary>
    /// Returns the community link with the given id, or null when no link has that id.
    /// </summary>
    CommunityLink? FindLink(string linkId);

    /// <summary>
    /// The resource key of the web view document that backs a community link.
    /// </summary>
    ResourceKey GetLinkResource(CommunityLink link);

    /// <summary>
    /// Writes the canonical web view document for a community link, replacing whatever the file held.
    /// </summary>
    Task<Result> WriteLinkDocumentAsync(CommunityLink link);
}
