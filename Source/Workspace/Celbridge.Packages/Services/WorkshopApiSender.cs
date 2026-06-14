using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Celbridge.Credentials;

namespace Celbridge.Packages;

/// <summary>
/// Sends authenticated requests to the workshop server's REST API. Shared by the
/// package and page API clients so the connection and auth handling lives in one
/// place rather than being duplicated per client.
/// </summary>
internal sealed class WorkshopApiSender : IDisposable
{
    private const string ApiKeyScheme = "Api-Key";

    private readonly ICredentialService _credentialService;
    private readonly HttpClient _httpClient;

    public WorkshopApiSender(ICredentialService credentialService, HttpMessageHandler messageHandler)
    {
        _credentialService = credentialService;
        _httpClient = new HttpClient(messageHandler);
    }

    // Builds and sends one authenticated request. Fails without sending when no
    // connection is stored or the stored URL is unusable, and maps 401 to a
    // single actionable message here so no call site can leak the key.
    public async Task<Result<HttpResponseMessage>> SendAsync(HttpMethod method, string relativePath, HttpContent? content = null)
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

    // The validated base URI of the configured workshop, so callers can resolve a
    // server-returned relative URL (e.g. a page's served path) to an absolute one.
    public async Task<Result<Uri>> GetBaseUriAsync()
    {
        var connectionResult = await _credentialService.GetWorkshopConnectionAsync();
        if (connectionResult.IsFailure)
        {
            return Result.Fail("Failed to read the Workshop connection from the credential store")
                .WithErrors(connectionResult);
        }

        return ValidateWorkshopUrl(connectionResult.Value.WorkshopUrl);
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

    public static async Task<Result<T>> ParseJsonAsync<T>(HttpResponseMessage response, string payloadDescription) where T : notnull
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

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
