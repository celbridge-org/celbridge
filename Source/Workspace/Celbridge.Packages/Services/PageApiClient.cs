using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using Celbridge.Settings;

namespace Celbridge.Packages;

/// <summary>
/// IPageApiClient backed by WorkshopApiSender, which it shares with the package
/// client for the common connection plumbing.
/// </summary>
public class PageApiClient : IPageApiClient, IDisposable
{
    private readonly WorkshopApiSender _sender;

    public PageApiClient(ISettingsService settingsService)
        : this(settingsService, new HttpClientHandler())
    {
    }

    /// <summary>
    /// Creates a client over an explicit message handler so tests can serve
    /// canned responses without a network.
    /// </summary>
    public PageApiClient(ISettingsService settingsService, HttpMessageHandler messageHandler)
    {
        _sender = new WorkshopApiSender(settingsService, messageHandler);
    }

    public async Task<Result<RemotePage>> PublishPageAsync(byte[] zipData, string path, string? author = null)
    {
        using var content = new MultipartFormDataContent();

        var fileContent = new ByteArrayContent(zipData);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", "page.zip");

        // The publish path is also carried inside the bundle's manifest; sending
        // it as a field too lets the server read it without parsing the manifest,
        // so the manifest can later be dropped from the upload.
        if (!string.IsNullOrEmpty(path))
        {
            content.Add(new StringContent(path, Encoding.UTF8), "path");
        }

        if (!string.IsNullOrEmpty(author))
        {
            content.Add(new StringContent(author, Encoding.UTF8), "author");
        }

        var sendResult = await _sender.SendAsync(HttpMethod.Post, "api/pages", content);
        if (sendResult.IsFailure)
        {
            return Result.Fail("Failed to publish page").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return Result.Fail(
                "A page is already published at this path. Unpublish the existing page before publishing over it, " +
                "or change the [publish].path in pages.toml.");
        }

        if (!response.IsSuccessStatusCode)
        {
            // The error body carries the server's validation message (e.g. a bad
            // bundle); it never contains credential material.
            var errorBody = await response.Content.ReadAsStringAsync();
            return Result.Fail($"Failed to publish page (HTTP {(int)response.StatusCode}): {errorBody}");
        }

        var parseResult = await WorkshopApiSender.ParseJsonAsync<PageDto>(response, "page publish");
        if (parseResult.IsFailure)
        {
            return Result.Fail(parseResult);
        }

        var baseUri = await TryGetBaseUriAsync();
        return ToPage(parseResult.Value, baseUri);
    }

    public async Task<Result<IReadOnlyList<RemotePage>>> ListPagesAsync()
    {
        var sendResult = await _sender.SendAsync(HttpMethod.Get, "api/pages");
        if (sendResult.IsFailure)
        {
            return Result.Fail("Failed to list workshop pages").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to list workshop pages (HTTP {(int)response.StatusCode})");
        }

        var parseResult = await WorkshopApiSender.ParseJsonAsync<List<PageDto>>(response, "page list");
        if (parseResult.IsFailure)
        {
            return Result.Fail(parseResult);
        }
        var entries = parseResult.Value;

        var baseUri = await TryGetBaseUriAsync();
        var pages = new List<RemotePage>(entries.Count);
        foreach (var entry in entries)
        {
            pages.Add(ToPage(entry, baseUri));
        }

        return pages.OkResult<IReadOnlyList<RemotePage>>();
    }

    public async Task<Result<RemotePage>> GetPageAsync(string path)
    {
        var sendResult = await _sender.SendAsync(HttpMethod.Get, $"api/pages/{EscapePagePath(path)}");
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to get workshop page '{path}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Fail($"No page is published at '{path}' on the workshop.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to get workshop page '{path}' (HTTP {(int)response.StatusCode})");
        }

        var parseResult = await WorkshopApiSender.ParseJsonAsync<PageDto>(response, "page detail");
        if (parseResult.IsFailure)
        {
            return Result.Fail(parseResult);
        }

        var baseUri = await TryGetBaseUriAsync();
        return ToPage(parseResult.Value, baseUri);
    }

    public async Task<Result> UnpublishPageAsync(string path)
    {
        var sendResult = await _sender.SendAsync(HttpMethod.Delete, $"api/pages/{EscapePagePath(path)}");
        if (sendResult.IsFailure)
        {
            return Result.Fail($"Failed to unpublish page '{path}'").WithErrors(sendResult);
        }

        using var response = sendResult.Value;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Fail($"No page is published at '{path}' on the workshop.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return Result.Fail($"Failed to unpublish page '{path}' (HTTP {(int)response.StatusCode})");
        }

        return Result.Ok();
    }

    // The publish path is multi-segment (e.g. "my-site/home"). The slashes are
    // path separators that must survive into the URL, so each segment is escaped
    // individually rather than escaping the whole string (which would encode the
    // separators). Empty segments from a leading, trailing, or doubled slash are
    // dropped so the endpoint path stays well formed.
    private static string EscapePagePath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var escaped = segments.Select(Uri.EscapeDataString);
        return string.Join('/', escaped);
    }

    // The configured workshop base, used to absolutize a relative page URL.
    // Returns null when it cannot be read, in which case the server's URL passes
    // through unchanged rather than failing the call.
    private async Task<Uri?> TryGetBaseUriAsync()
    {
        var baseUriResult = await _sender.GetBaseUriAsync();
        return baseUriResult.IsSuccess ? baseUriResult.Value : null;
    }

    // Resolves the server's URL against the workshop base, so a relative URL
    // (e.g. "/pages/org/path/") becomes a full address. An already-absolute URL,
    // and the no-base fallback, pass through unchanged.
    private static RemotePage ToPage(PageDto dto, Uri? baseUri)
    {
        var url = dto.Url ?? string.Empty;
        if (baseUri is not null
            && !string.IsNullOrEmpty(url)
            && Uri.TryCreate(baseUri, url, out var absoluteUri))
        {
            url = absoluteUri.ToString();
        }

        return new RemotePage(
            dto.Path ?? string.Empty,
            url,
            dto.PublishedAt,
            dto.PublishedBy ?? string.Empty,
            dto.ContentHash ?? string.Empty);
    }

    private record PageDto(
        [property: JsonPropertyName("path")] string? Path,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("published_at")] DateTime PublishedAt,
        [property: JsonPropertyName("published_by")] string? PublishedBy,
        [property: JsonPropertyName("content_hash")] string? ContentHash);

    public void Dispose()
    {
        _sender.Dispose();
    }
}
