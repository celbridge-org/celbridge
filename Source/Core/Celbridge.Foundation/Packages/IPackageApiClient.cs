namespace Celbridge.Packages;

/// <summary>
/// Summary of a package version, as returned in package listings.
/// </summary>
public record RemoteVersionSummary(int Version, string Author, DateTime Date);

/// <summary>
/// A package listed by the workshop. LatestVersion is null when the package
/// has no live versions.
/// </summary>
public record RemotePackageSummary(
    string Name,
    DateTime CreatedAt,
    RemoteVersionSummary? LatestVersion,
    int VersionsCount);

/// <summary>
/// A single immutable version of a workshop package. Version numbers are
/// assigned by the server in publish order. Deleted is true when the version's
/// content has been removed; the version record, number, and content hash are
/// retained so the record stays verifiable.
/// </summary>
public record RemotePackageVersion(
    int Version,
    string Author,
    DateTime Date,
    bool Deleted,
    string ContentHash,
    string Summary);

/// <summary>
/// A named pointer at a package version (e.g. "latest", "stable").
/// </summary>
public record RemotePackageAlias(string Alias, int Version);

/// <summary>
/// Full metadata for a workshop package, including its versions and aliases.
/// </summary>
public record RemotePackageDetails(
    string Name,
    DateTime CreatedAt,
    IReadOnlyList<RemotePackageVersion> Versions,
    IReadOnlyList<RemotePackageAlias> Aliases);

/// <summary>
/// Server receipt for a published version, carrying the assigned version number.
/// </summary>
public record RemotePublishReceipt(
    string PackageName,
    int Version,
    string Author,
    string ContentHash);

/// <summary>
/// The result of probing the workshop connection.
/// </summary>
public enum ConnectionCheckOutcome
{
    /// <summary>
    /// The workshop responded and accepted the stored key.
    /// </summary>
    Connected,

    /// <summary>
    /// The workshop was reached but rejected the stored key (HTTP 401).
    /// </summary>
    Unauthorized,

    /// <summary>
    /// The workshop could not be reached or did not return a usable response
    /// (offline, timed out, or a server error), so the key could not be verified.
    /// </summary>
    Unreachable,
}

/// <summary>
/// Client for the workshop server's package REST API. Callers do not supply
/// credentials; requests are authenticated from the ambient workshop
/// configuration, and credential values never appear in this API's parameters,
/// results, or error messages.
/// </summary>
public interface IPackageApiClient
{
    /// <summary>
    /// Lists the packages available on the workshop.
    /// </summary>
    Task<Result<IReadOnlyList<RemotePackageSummary>>> ListPackagesAsync();

    /// <summary>
    /// Probes the workshop with one authenticated request and classifies the
    /// outcome. Always resolves to a known outcome rather than throwing or
    /// failing, so callers can tell a rejected key from an unreachable workshop.
    /// </summary>
    Task<ConnectionCheckOutcome> CheckConnectionAsync();

    /// <summary>
    /// Gets a package's full metadata, including its versions and aliases.
    /// </summary>
    Task<Result<RemotePackageDetails>> GetPackageAsync(string packageName);

    /// <summary>
    /// Publishes a new package version from ZIP data, with an optional change
    /// summary and the publishing author. The first publish of a new name
    /// registers the package implicitly. The author is recorded by the workshop
    /// as the version's publisher.
    /// </summary>
    Task<Result<RemotePublishReceipt>> PublishVersionAsync(string packageName, byte[] zipData, string? summary = null, string? author = null);

    /// <summary>
    /// Downloads the ZIP data for a specific package version.
    /// </summary>
    Task<Result<byte[]>> DownloadVersionAsync(string packageName, int version);

    /// <summary>
    /// Downloads the ZIP data for the latest live version of a package.
    /// </summary>
    Task<Result<byte[]>> DownloadLatestAsync(string packageName);

    /// <summary>
    /// Creates an alias pointing at a version, or moves an existing alias.
    /// </summary>
    Task<Result> SetAliasAsync(string packageName, string alias, int version);

    /// <summary>
    /// Removes an alias. The version the alias pointed at is unaffected.
    /// </summary>
    Task<Result> RemoveAliasAsync(string packageName, string alias);

    /// <summary>
    /// Deletes a published version's content. Its version number and content hash
    /// remain reserved, so the number is never reused and the record stays
    /// verifiable. Irreversible from the client.
    /// </summary>
    Task<Result> DeleteVersionAsync(string packageName, int version);

    /// <summary>
    /// Deletes a whole package and all its versions from the workshop.
    /// Irreversible from the client.
    /// </summary>
    Task<Result> DeletePackageAsync(string packageName);

    /// <summary>
    /// Gets the plain text publish history of a package as of the given version.
    /// </summary>
    Task<Result<string>> GetVersionHistoryAsync(string packageName, int version);
}
