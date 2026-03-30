namespace Celbridge.Packages;

/// <summary>
/// An entry returned by the package API representing a file on the server.
/// </summary>
public partial record PackageApiEntry(int Id, string FileName, long FileSize, DateTime UploadedAt);

/// <summary>
/// Client for the Celbridge package registry REST API.
/// </summary>
public interface IPackageApiClient
{
    /// <summary>
    /// Lists all packages available on the remote registry.
    /// </summary>
    Task<Result<List<PackageApiEntry>>> ListPackagesAsync();

    /// <summary>
    /// Downloads a package zip by file name from the remote registry.
    /// </summary>
    Task<Result<byte[]>> DownloadPackageAsync(int fileId);

    /// <summary>
    /// Uploads a package zip to the remote registry.
    /// </summary>
    Task<Result> UploadPackageAsync(string fileName, byte[] zipData);
}
