using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Celbridge.Packages;

/// <summary>
/// Communicates with the Django-based package registry REST API.
/// Credentials are read from the PackageApiCredentials partial class.
/// </summary>
public class PackageApiClient : IPackageApiClient
{
    private HttpClient? _httpClient;
    private CookieContainer? _cookieContainer;

    public async Task<Result<List<PackageApiEntry>>> ListPackagesAsync()
    {
        var loginResult = await EnsureLoggedInAsync();
        if (loginResult.IsFailure)
        {
            return Result<List<PackageApiEntry>>.Fail(loginResult.Error);
        }

        try
        {
            var response = await _httpClient!.GetAsync("api/files/");
            if (!response.IsSuccessStatusCode)
            {
                return Result<List<PackageApiEntry>>.Fail(
                    $"Failed to list packages (HTTP {(int)response.StatusCode})");
            }

            var json = await response.Content.ReadAsStringAsync();
            var entries = JsonSerializer.Deserialize<List<ApiFileEntry>>(json, JsonOptions);
            if (entries is null)
            {
                return Result<List<PackageApiEntry>>.Fail("Failed to parse package list response");
            }

            var packages = new List<PackageApiEntry>();
            foreach (var entry in entries)
            {
                var fileName = Path.GetFileName(entry.File);
                packages.Add(new PackageApiEntry(entry.FileId, fileName, entry.FileSize, entry.UploadedAt));
            }

            return Result<List<PackageApiEntry>>.Ok(packages);
        }
        catch (HttpRequestException exception)
        {
            return Result<List<PackageApiEntry>>.Fail($"Network error listing packages: {exception.Message}");
        }
    }

    public async Task<Result<byte[]>> DownloadPackageAsync(int fileId)
    {
        var loginResult = await EnsureLoggedInAsync();
        if (loginResult.IsFailure)
        {
            return Result<byte[]>.Fail(loginResult.Error);
        }

        try
        {
            var response = await _httpClient!.GetAsync($"api/files/{fileId}/");
            if (!response.IsSuccessStatusCode)
            {
                return Result<byte[]>.Fail(
                    $"Failed to download package (HTTP {(int)response.StatusCode})");
            }

            var data = await response.Content.ReadAsByteArrayAsync();
            return Result<byte[]>.Ok(data);
        }
        catch (HttpRequestException exception)
        {
            return Result<byte[]>.Fail($"Network error downloading package: {exception.Message}");
        }
    }

    public async Task<Result> UploadPackageAsync(string fileName, byte[] zipData)
    {
        var loginResult = await EnsureLoggedInAsync();
        if (loginResult.IsFailure)
        {
            return Result.Fail(loginResult.Error);
        }

        try
        {
            var csrfToken = GetCsrfToken();

            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(zipData);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", fileName);

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/upload/");
            request.Content = content;
            if (!string.IsNullOrEmpty(csrfToken))
            {
                request.Headers.Add("X-CSRFToken", csrfToken);
            }

            var response = await _httpClient!.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return Result.Fail(
                    $"Failed to upload package (HTTP {(int)response.StatusCode}): {errorBody}");
            }

            return Result.Ok();
        }
        catch (HttpRequestException exception)
        {
            return Result.Fail($"Network error uploading package: {exception.Message}");
        }
    }

    private async Task<Result> EnsureLoggedInAsync()
    {
        if (_httpClient is not null)
        {
            return Result.Ok();
        }

        var baseUrl = PackageApiCredentials.BaseUrl;
        var username = PackageApiCredentials.Username;
        var password = PackageApiCredentials.Password;

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return Result.Fail(
                "Package API credentials are not configured. " +
                "Add a PackageApiCredentials.private.cs file with the BaseUrl, Username, and Password values.");
        }

        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += '/';
        }

        try
        {
            _cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                UseCookies = true
            };

            var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl)
            };

            // Set Basic Auth on all requests, matching the Django API's authentication
            var basicAuthValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", basicAuthValue);

            // GET the login page to obtain the CSRF token
            var loginPageResponse = await httpClient.GetAsync("admin/login/");
            loginPageResponse.EnsureSuccessStatusCode();

            var csrfToken = GetCsrfTokenFromContainer(baseUrl);

            // POST credentials with the CSRF token
            var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = username,
                ["password"] = password,
                ["csrfmiddlewaretoken"] = csrfToken ?? "",
                ["next"] = "/admin/"
            });

            var loginResponse = await httpClient.PostAsync("admin/login/", loginContent);
            var loginBody = await loginResponse.Content.ReadAsStringAsync();

            if (loginBody.Contains("Log in"))
            {
                return Result.Fail("Package API login failed. Check credentials in PackageApiCredentials.private.cs.");
            }

            _httpClient = httpClient;
            return Result.Ok();
        }
        catch (HttpRequestException exception)
        {
            _cookieContainer = null;
            return Result.Fail($"Failed to connect to package API: {exception.Message}");
        }
    }

    private string? GetCsrfToken()
    {
        if (_cookieContainer is null || _httpClient?.BaseAddress is null)
        {
            return null;
        }

        return GetCsrfTokenFromContainer(_httpClient.BaseAddress.ToString());
    }

    private string? GetCsrfTokenFromContainer(string baseUrl)
    {
        if (_cookieContainer is null)
        {
            return null;
        }

        var cookies = _cookieContainer.GetCookies(new Uri(baseUrl));
        var csrfCookie = cookies["csrftoken"];
        return csrfCookie?.Value;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private record ApiFileEntry(
        [property: JsonPropertyName("id")] int FileId,
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("file_size")] long FileSize,
        [property: JsonPropertyName("uploaded_at")] DateTime UploadedAt);

    public void Dispose()
    {
        _httpClient?.Dispose();
        _httpClient = null;
        _cookieContainer = null;
    }
}
