using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Celbridge.Credentials;
using Celbridge.Settings;

namespace Celbridge.Packages;

/// <summary>
/// Communicates with the workshop server's package REST API. The Workshop URL is
/// read from settings and the Workshop Key from the credential store on every
/// request, so a connection change in Settings takes effect without a restart.
/// The key is never included in results or error messages.
/// </summary>
public class PackageApiClient : IPackageApiClient, IDisposable
{
    private readonly WorkshopApiSender _sender;

    public PackageApiClient(ICredentialService credentialService, IEditorSettings editorSettings)
        : this(credentialService, editorSettings, new HttpClientHandler())
    {
    }

    /// <summary>
    /// Creates a client over an explicit message handler so tests can serve
    /// canned responses without a network.
    /// </summary>
    public PackageApiClient(ICredentialService credentialService, IEditorSettings editorSettings, HttpMessageHandler messageHandler)
    {
        _sender = new WorkshopApiSender(credentialService, editorSettings, messageHandler);
    }

    public async Task<Result<IReadOnlyList<RemotePackageSummary>>> ListPackagesAsync()
    {
        var sendResult = await _sender.SendAsync(HttpMethod.Get, "api/packages/");
        if (sendResult.IsFailure)
        {
            return Result.Fail("Failed to list workshop packages").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to list workshop packages (HTTP {(int)response.StatusCode})");
        }

        var parseResult = await WorkshopApiSender.ParseJsonAsync<List<PackageSummaryDto>>(response, "package list");
        if (parseResult.IsFailure)
        {
            return Result.Fail(parseResult);
        }
        var entries = parseResult.Value;

        var summaries = new List<RemotePackageSummary>(entries.Count);
        foreach (var entry in entries)
        {
            summaries.Add(ToPackageSummary(entry));
        }

        return summaries.OkResult<IReadOnlyList<RemotePackageSummary>>();
    }

    public async Task<Result<RemotePackageDetails>> GetPackageAsync(string packageName)
    {
        var sendResult = await _sender.SendAsync(HttpMethod.Get, $"api/packages/{Uri.EscapeDataString(packageName)}/");
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to get workshop package '{packageName}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Fail($"Package '{packageName}' was not found on the workshop.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to get workshop package '{packageName}' (HTTP {(int)response.StatusCode})");
        }

        var parseResult = await WorkshopApiSender.ParseJsonAsync<PackageDetailsDto>(response, "package metadata");
        if (parseResult.IsFailure)
        {
            return Result.Fail(parseResult);
        }
        var details = parseResult.Value;

        var versions = new List<RemotePackageVersion>();
        foreach (var version in details.Versions ?? [])
        {
            versions.Add(new RemotePackageVersion(
                version.Version,
                version.Author ?? string.Empty,
                version.Date,
                version.Deleted,
                version.ContentHash ?? string.Empty,
                version.Summary ?? string.Empty));
        }

        var aliases = new List<RemotePackageAlias>();
        foreach (var alias in details.Aliases ?? [])
        {
            // The live workshop returns the alias name under "name", while
            // earlier drafts of the wire contract (and our canned test
            // payloads) used "alias". The DTO carries both, and we pick
            // whichever the server populated so install-by-alias works
            // either way until the wire contract is settled.
            var aliasName = alias.Name ?? alias.Alias ?? string.Empty;
            aliases.Add(new RemotePackageAlias(aliasName, alias.Version));
        }

        return new RemotePackageDetails(
            details.Name ?? packageName,
            details.CreatedAt,
            versions.AsReadOnly(),
            aliases.AsReadOnly());
    }

    public async Task<Result<RemotePublishReceipt>> PublishVersionAsync(string packageName, byte[] zipData, string? summary = null, string? author = null)
    {
        using var content = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(zipData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", $"{packageName}.zip");

        if (!string.IsNullOrEmpty(summary))
        {
            content.Add(new StringContent(summary, Encoding.UTF8), "summary");
        }

        if (!string.IsNullOrEmpty(author))
        {
            content.Add(new StringContent(author, Encoding.UTF8), "author");
        }

        var sendResult = await _sender.SendAsync(HttpMethod.Post, $"api/packages/{Uri.EscapeDataString(packageName)}/versions/", content);
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to publish package '{packageName}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (!response.IsSuccessStatusCode)
        {
            // The error body carries the server's validation message (e.g. a bad
            // bundle); it never contains credential material.
            var errorBody = await response.Content.ReadAsStringAsync();
            return Result.Fail($"Failed to publish package '{packageName}' (HTTP {(int)response.StatusCode}): {errorBody}");
        }

        var parseResult = await WorkshopApiSender.ParseJsonAsync<PublishReceiptDto>(response, "publish receipt");
        if (parseResult.IsFailure)
        {
            return Result.Fail(parseResult);
        }
        var receipt = parseResult.Value;

        return new RemotePublishReceipt(
            receipt.Package ?? packageName,
            receipt.Version,
            receipt.Author ?? string.Empty,
            receipt.ContentHash ?? string.Empty);
    }

    public async Task<Result<byte[]>> DownloadVersionAsync(string packageName, int version)
    {
        var sendResult = await _sender.SendAsync(HttpMethod.Get, $"api/packages/{Uri.EscapeDataString(packageName)}/versions/{version}/download/");
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to download version {version} of package '{packageName}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (response.StatusCode == HttpStatusCode.Gone)
        {
            return Result.Fail($"Version {version} of package '{packageName}' has been deleted and can no longer be downloaded.");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Fail($"Version {version} of package '{packageName}' was not found on the workshop.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to download version {version} of package '{packageName}' (HTTP {(int)response.StatusCode})");
        }

        var data = await response.Content.ReadAsByteArrayAsync();
        return data;
    }

    public async Task<Result<byte[]>> DownloadLatestAsync(string packageName)
    {
        var sendResult = await _sender.SendAsync(HttpMethod.Get, $"api/packages/{Uri.EscapeDataString(packageName)}/latest/");
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to download the latest version of package '{packageName}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Fail($"Package '{packageName}' has no live version to download. It may not exist on the workshop, or every version may have been deleted.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to download the latest version of package '{packageName}' (HTTP {(int)response.StatusCode})");
        }

        var data = await response.Content.ReadAsByteArrayAsync();
        return data;
    }

    public async Task<Result> SetAliasAsync(string packageName, string alias, int version)
    {
        var body = JsonSerializer.Serialize(new AliasVersionBodyDto(version));
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        var sendResult = await _sender.SendAsync(HttpMethod.Put, $"api/packages/{Uri.EscapeDataString(packageName)}/aliases/{Uri.EscapeDataString(alias)}/", content);
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to set alias '{alias}' on package '{packageName}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The server returns 404 for both "package not found" and "version
            // does not exist on this package"; the more common scripting
            // mistake is the version target, so name it first.
            return Result.Fail($"Cannot point alias '{alias}' at version {version}: version {version} does not exist on package '{packageName}' (or the package itself was not found).");
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to set alias '{alias}' to version {version} of package '{packageName}' (HTTP {(int)response.StatusCode})");
        }

        return Result.Ok();
    }

    public async Task<Result> RemoveAliasAsync(string packageName, string alias)
    {
        var sendResult = await _sender.SendAsync(HttpMethod.Delete, $"api/packages/{Uri.EscapeDataString(packageName)}/aliases/{Uri.EscapeDataString(alias)}/");
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to remove alias '{alias}' from package '{packageName}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Fail($"Alias '{alias}' was not found on package '{packageName}'.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to remove alias '{alias}' from package '{packageName}' (HTTP {(int)response.StatusCode})");
        }

        return Result.Ok();
    }

    public async Task<Result> DeleteVersionAsync(string packageName, int version)
    {
        using var content = BuildDeleteReasonContent();

        var sendResult = await _sender.SendAsync(HttpMethod.Delete, $"api/packages/{Uri.EscapeDataString(packageName)}/versions/{version}/", content);
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to delete version {version} of package '{packageName}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Fail($"Version {version} of package '{packageName}' was not found on the workshop.");
        }

        if (response.StatusCode == HttpStatusCode.Gone)
        {
            return Result.Fail($"Version {version} of package '{packageName}' has already been deleted.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to delete version {version} of package '{packageName}' (HTTP {(int)response.StatusCode})");
        }

        return Result.Ok();
    }

    public async Task<Result> DeletePackageAsync(string packageName)
    {
        using var content = BuildDeleteReasonContent();

        var sendResult = await _sender.SendAsync(HttpMethod.Delete, $"api/packages/{Uri.EscapeDataString(packageName)}/", content);
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to delete package '{packageName}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Fail($"Package '{packageName}' was not found on the workshop.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to delete package '{packageName}' (HTTP {(int)response.StatusCode})");
        }

        return Result.Ok();
    }

    // The delete endpoints accept a {reason} audit note. The tools do not collect
    // one, so a fixed note is sent. A server that ignores the field is unaffected.
    private static StringContent BuildDeleteReasonContent()
    {
        var body = JsonSerializer.Serialize(new DeleteReasonBodyDto("Deleted via Celbridge."));
        return new StringContent(body, Encoding.UTF8, "application/json");
    }

    public async Task<Result<string>> GetVersionHistoryAsync(string packageName, int version)
    {
        var sendResult = await _sender.SendAsync(HttpMethod.Get, $"api/packages/{Uri.EscapeDataString(packageName)}/versions/{version}/history/");
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to get the history of package '{packageName}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to get the history of package '{packageName}' as of version {version} (HTTP {(int)response.StatusCode})");
        }

        var history = await response.Content.ReadAsStringAsync();
        return history;
    }

    private static RemotePackageSummary ToPackageSummary(PackageSummaryDto entry)
    {
        RemoteVersionSummary? latestVersion = null;
        if (entry.LatestVersion is not null)
        {
            latestVersion = new RemoteVersionSummary(
                entry.LatestVersion.Version,
                entry.LatestVersion.Author ?? string.Empty,
                entry.LatestVersion.Date);
        }

        return new RemotePackageSummary(
            entry.Name ?? string.Empty,
            entry.CreatedAt,
            latestVersion,
            entry.VersionsCount);
    }

    private record VersionSummaryDto(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("author")] string? Author,
        [property: JsonPropertyName("date")] DateTime Date);

    private record PackageSummaryDto(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("created_at")] DateTime CreatedAt,
        [property: JsonPropertyName("latest_version")] VersionSummaryDto? LatestVersion,
        [property: JsonPropertyName("versions_count")] int VersionsCount);

    // The server's wire field is still "tombstoned". The client maps it to a
    // Deleted flag because Celbridge does not model a dead-but-retained state.
    private record VersionDetailDto(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("author")] string? Author,
        [property: JsonPropertyName("date")] DateTime Date,
        [property: JsonPropertyName("tombstoned")] bool Deleted,
        [property: JsonPropertyName("content_hash")] string? ContentHash,
        [property: JsonPropertyName("summary")] string? Summary);

    private record AliasDto(
        [property: JsonPropertyName("alias")] string? Alias,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("version")] int Version);

    private record PackageDetailsDto(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("created_at")] DateTime CreatedAt,
        [property: JsonPropertyName("versions")] List<VersionDetailDto>? Versions,
        [property: JsonPropertyName("aliases")] List<AliasDto>? Aliases);

    private record PublishReceiptDto(
        [property: JsonPropertyName("package")] string? Package,
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("author")] string? Author,
        [property: JsonPropertyName("download_url")] string? DownloadUrl,
        [property: JsonPropertyName("content_hash")] string? ContentHash);

    private record AliasVersionBodyDto(
        [property: JsonPropertyName("version")] int Version);

    private record DeleteReasonBodyDto(
        [property: JsonPropertyName("reason")] string Reason);

    public void Dispose()
    {
        _sender.Dispose();
    }
}
