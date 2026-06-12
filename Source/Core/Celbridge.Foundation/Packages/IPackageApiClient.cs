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
/// assigned by the server in publish order.
/// </summary>
public record RemotePackageVersion(
    int Version,
    string Author,
    DateTime Date,
    bool Tombstoned,
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
/// Client for the workshop server's package REST API. The Workshop URL and
/// Application Key are read from the credential store at request time;
/// credential values never appear in parameters, results, or error messages.
/// Destructive administrative operations (tombstoning a version, deleting a
/// package) are deliberately not part of this surface.
/// </summary>
public interface IPackageApiClient
{
    /// <summary>
    /// Lists the packages available on the workshop.
    /// </summary>
    Task<Result<IReadOnlyList<RemotePackageSummary>>> ListPackagesAsync();

    /// <summary>
    /// Gets a package's full metadata, including its versions and aliases.
    /// </summary>
    Task<Result<RemotePackageDetails>> GetPackageAsync(string packageName);

    /// <summary>
    /// Publishes a new package version from ZIP data, with an optional change
    /// summary. The first publish of a new name registers the package implicitly.
    /// </summary>
    Task<Result<RemotePublishReceipt>> PublishVersionAsync(string packageName, byte[] zipData, string? summary = null);

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
    /// Gets the plain text publish history of a package as of the given version.
    /// </summary>
    Task<Result<string>> GetVersionHistoryAsync(string packageName, int version);
}
