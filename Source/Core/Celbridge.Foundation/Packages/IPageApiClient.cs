namespace Celbridge.Packages;

/// <summary>
/// A page published by the workshop. Path is the served path; Url is the public
/// address the rendered site is served at.
/// </summary>
public record RemotePage(
    string Path,
    string Url,
    DateTime PublishedAt,
    string PublishedBy,
    string ContentHash);

/// <summary>
/// Client for the workshop server's page REST API. Pages are publish-only:
/// there is no endpoint to download a published page back.
/// </summary>
public interface IPageApiClient
{
    /// <summary>
    /// Publishes a page bundle as a new page and returns it. Path is the served
    /// path declared in the bundle's manifest; author records the publisher.
    /// </summary>
    Task<Result<RemotePage>> PublishPageAsync(byte[] zipData, string path, string? author = null);

    /// <summary>
    /// Lists the pages published to the workshop.
    /// </summary>
    Task<Result<IReadOnlyList<RemotePage>>> ListPagesAsync();

    /// <summary>
    /// Gets a published page by its served path.
    /// </summary>
    Task<Result<RemotePage>> GetPageAsync(string path);

    /// <summary>
    /// Unpublishes a page by its served path, removing its served content.
    /// </summary>
    Task<Result> UnpublishPageAsync(string path);
}
