using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Celbridge.Credentials;

namespace Celbridge.Packages;

/// <summary>
/// Communicates with the workshop server's package REST API. The Workshop URL
/// and Application Key are read from the credential store on every request, so
/// a connection change in Settings takes effect without a restart. The key is
/// never included in results or error messages.
/// </summary>
public class PackageApiClient : IPackageApiClient, IDisposable
{
    private const string ApiKeyScheme = "Api-Key";

    private readonly ICredentialService _credentialService;
    private readonly HttpClient _httpClient;

    public PackageApiClient(ICredentialService credentialService)
        : this(credentialService, new HttpClientHandler())
    {
    }

    /// <summary>
    /// Creates a client over an explicit message handler so tests can serve
    /// canned responses without a network.
    /// </summary>
    public PackageApiClient(ICredentialService credentialService, HttpMessageHandler messageHandler)
    {
        _credentialService = credentialService;
        _httpClient = new HttpClient(messageHandler);
    }

    public async Task<Result<IReadOnlyList<RemotePackageSummary>>> ListPackagesAsync()
    {
        var sendResult = await SendAsync(HttpMethod.Get, "api/packages/");
        if (sendResult.IsFailure)
        {
            return Result.Fail("Failed to list workshop packages").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to list workshop packages (HTTP {(int)response.StatusCode})");
        }

        var parseResult = await ParseJsonAsync<List<PackageSummaryDto>>(response, "package list");
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
        var sendResult = await SendAsync(HttpMethod.Get, $"api/packages/{Uri.EscapeDataString(packageName)}/");
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

        var parseResult = await ParseJsonAsync<PackageDetailsDto>(response, "package metadata");
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
                version.Tombstoned,
                version.ContentHash ?? string.Empty,
                version.Summary ?? string.Empty));
        }

        var aliases = new List<RemotePackageAlias>();
        foreach (var alias in details.Aliases ?? [])
        {
            aliases.Add(new RemotePackageAlias(alias.Alias ?? string.Empty, alias.Version));
        }

        return new RemotePackageDetails(
            details.Name ?? packageName,
            details.CreatedAt,
            versions.AsReadOnly(),
            aliases.AsReadOnly());
    }

    public async Task<Result<RemotePublishReceipt>> PublishVersionAsync(string packageName, byte[] zipData, string? summary = null)
    {
        using var content = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(zipData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", $"{packageName}.zip");

        if (!string.IsNullOrEmpty(summary))
        {
            content.Add(new StringContent(summary, Encoding.UTF8), "summary");
        }

        var sendResult = await SendAsync(HttpMethod.Post, $"api/packages/{Uri.EscapeDataString(packageName)}/versions/", content);
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

        var parseResult = await ParseJsonAsync<PublishReceiptDto>(response, "publish receipt");
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
        var sendResult = await SendAsync(HttpMethod.Get, $"api/packages/{Uri.EscapeDataString(packageName)}/versions/{version}/download/");
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to download version {version} of package '{packageName}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (response.StatusCode == HttpStatusCode.Gone)
        {
            return Result.Fail($"Version {version} of package '{packageName}' has been tombstoned and can no longer be downloaded.");
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
        var sendResult = await SendAsync(HttpMethod.Get, $"api/packages/{Uri.EscapeDataString(packageName)}/latest/");
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to download the latest version of package '{packageName}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Fail($"Package '{packageName}' has no live version to download. It may not exist on the workshop, or every version may have been tombstoned.");
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

        var sendResult = await SendAsync(HttpMethod.Put, $"api/packages/{Uri.EscapeDataString(packageName)}/aliases/{Uri.EscapeDataString(alias)}/", content);
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to set alias '{alias}' on package '{packageName}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to set alias '{alias}' to version {version} of package '{packageName}' (HTTP {(int)response.StatusCode})");
        }

        return Result.Ok();
    }

    public async Task<Result> RemoveAliasAsync(string packageName, string alias)
    {
        var sendResult = await SendAsync(HttpMethod.Delete, $"api/packages/{Uri.EscapeDataString(packageName)}/aliases/{Uri.EscapeDataString(alias)}/");
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

    public async Task<Result<string>> GetVersionHistoryAsync(string packageName, int version)
    {
        var sendResult = await SendAsync(HttpMethod.Get, $"api/packages/{Uri.EscapeDataString(packageName)}/versions/{version}/history/");
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

    // Builds and sends one authenticated request. Fails without sending when no
    // connection is stored or the stored URL is unusable, and maps 401 to a
    // single actionable message here so no call site can leak the key.
    private async Task<Result<HttpResponseMessage>> SendAsync(HttpMethod method, string relativePath, HttpContent? content = null)
    {
        var connectionResult = await _credentialService.GetWorkshopConnectionAsync();
        if (connectionResult.IsFailure)
        {
            return Result.Fail("Failed to read the Workshop connection from the credential store")
                .WithErrors(connectionResult);
        }
        var connection = connectionResult.Value;

        var baseUriResult = ValidateWorkshopUrl(connection.WorkshopUrl);
        if (baseUriResult.IsFailure)
        {
            return Result.Fail(baseUriResult);
        }
        var baseUri = baseUriResult.Value;

        var request = new HttpRequestMessage(method, new Uri(baseUri, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue(ApiKeyScheme, connection.ApplicationKey);
        if (content is not null)
        {
            request.Content = content;
        }

        try
        {
            var response = await _httpClient.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                return Result.Fail(
                    "The workshop rejected the stored Application Key (HTTP 401). " +
                    "The key may be invalid, revoked, or no longer linked to the workshop. " +
                    "Update the Workshop connection on the Settings page.");
            }

            return response;
        }
        catch (HttpRequestException exception)
        {
            return Result.Fail("A network error occurred while contacting the workshop")
                .WithException(exception);
        }
        catch (TaskCanceledException exception)
        {
            return Result.Fail("The workshop request timed out")
                .WithException(exception);
        }
    }

    // The Application Key is a bearer credential, so sending it over plain HTTP
    // would fully compromise it to any network observer. Loopback hosts are
    // exempt to support local development servers.
    private static Result<Uri> ValidateWorkshopUrl(string workshopUrl)
    {
        var urlText = workshopUrl.Trim();
        if (!urlText.EndsWith('/'))
        {
            // Ensure the trailing path segment is kept when combining with
            // relative endpoint paths.
            urlText += '/';
        }

        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var uri))
        {
            return Result.Fail("The stored Workshop URL is not a valid absolute URL. Update the Workshop connection on the Settings page.");
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return uri;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
        {
            return uri;
        }

        return Result.Fail("The Workshop URL must use HTTPS. Plain HTTP would expose the Application Key to network observers and is only permitted for localhost development servers.");
    }

    private static async Task<Result<T>> ParseJsonAsync<T>(HttpResponseMessage response, string payloadDescription) where T : notnull
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<T>(json);
            if (parsed is null)
            {
                return Result.Fail($"Failed to parse the workshop {payloadDescription} response");
            }

            return parsed;
        }
        catch (JsonException exception)
        {
            return Result<T>.Fail($"Failed to parse the workshop {payloadDescription} response")
                .WithException(exception);
        }
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

    private record VersionDetailDto(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("author")] string? Author,
        [property: JsonPropertyName("date")] DateTime Date,
        [property: JsonPropertyName("tombstoned")] bool Tombstoned,
        [property: JsonPropertyName("content_hash")] string? ContentHash,
        [property: JsonPropertyName("summary")] string? Summary);

    private record AliasDto(
        [property: JsonPropertyName("alias")] string? Alias,
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

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
