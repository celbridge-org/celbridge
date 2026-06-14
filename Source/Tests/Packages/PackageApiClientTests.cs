using System.Net;
using System.Text;
using Celbridge.Credentials;
using Celbridge.Packages;

namespace Celbridge.Tests.Packages;

[TestFixture]
public class PackageApiClientTests
{
    private const string TestApplicationKey = "kpf_testkey_supersecret";

    private StubMessageHandler _messageHandler = null!;
    private ICredentialService _credentialService = null!;
    private PackageApiClient _client = null!;

    [SetUp]
    public void Setup()
    {
        _messageHandler = new StubMessageHandler();
        _credentialService = Substitute.For<ICredentialService>();
        SetStoredConnection("https://workshop.example.com", TestApplicationKey);
        _client = new PackageApiClient(_credentialService, _messageHandler);
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
    }

    [Test]
    public async Task ListPackages_SendsApiKeyHeaderAndParsesSummaries()
    {
        _messageHandler.Responder = _ => JsonResponse("""
            [
              {
                "name": "my-widget",
                "created_at": "2026-01-10T12:00:00Z",
                "latest_version": { "version": 3, "author": "alice", "date": "2026-02-01T09:30:00Z" },
                "versions_count": 3
              },
              {
                "name": "empty-package",
                "created_at": "2026-01-12T08:00:00Z",
                "latest_version": null,
                "versions_count": 0
              }
            ]
            """);

        var result = await _client.ListPackagesAsync();

        result.IsSuccess.Should().BeTrue();

        var request = _messageHandler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/packages/"));
        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Api-Key");
        request.Headers.Authorization.Parameter.Should().Be(TestApplicationKey);

        var packages = result.Value;
        packages.Should().HaveCount(2);
        packages[0].Name.Should().Be("my-widget");
        packages[0].VersionsCount.Should().Be(3);
        packages[0].LatestVersion.Should().NotBeNull();
        packages[0].LatestVersion!.Version.Should().Be(3);
        packages[0].LatestVersion!.Author.Should().Be("alice");
        packages[1].Name.Should().Be("empty-package");
        packages[1].LatestVersion.Should().BeNull();
    }

    [Test]
    public async Task GetPackage_ParsesVersionsAndAliases()
    {
        _messageHandler.Responder = _ => JsonResponse("""
            {
              "name": "my-widget",
              "created_at": "2026-01-10T12:00:00Z",
              "versions": [
                { "version": 1, "author": "alice", "date": "2026-01-10T12:00:00Z", "tombstoned": true, "content_hash": "aaa111", "summary": "Initial release" },
                { "version": 2, "author": "bob", "date": "2026-02-01T09:30:00Z", "tombstoned": false, "content_hash": "bbb222", "summary": "Bug fixes" }
              ],
              "aliases": [
                { "alias": "latest", "version": 2 },
                { "alias": "stable", "version": 2 }
              ]
            }
            """);

        var result = await _client.GetPackageAsync("my-widget");

        result.IsSuccess.Should().BeTrue();

        var request = _messageHandler.Requests.Single();
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/packages/my-widget/"));

        var details = result.Value;
        details.Name.Should().Be("my-widget");
        details.Versions.Should().HaveCount(2);
        details.Versions[0].Version.Should().Be(1);
        details.Versions[0].Deleted.Should().BeTrue();
        details.Versions[0].Summary.Should().Be("Initial release");
        details.Versions[1].Author.Should().Be("bob");
        details.Versions[1].ContentHash.Should().Be("bbb222");
        details.Aliases.Should().HaveCount(2);
        details.Aliases[0].Alias.Should().Be("latest");
        details.Aliases[0].Version.Should().Be(2);
    }

    [Test]
    public async Task GetPackage_UnknownPackage_Fails()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var result = await _client.GetPackageAsync("missing-package");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("missing-package");
        result.MessageChain.Should().Contain("not found");
    }

    [Test]
    public async Task PublishVersion_SendsMultipartAndParsesReceipt()
    {
        _messageHandler.Responder = _ => JsonResponse("""
            {
              "package": "my-widget",
              "version": 4,
              "author": "alice",
              "download_url": "https://workshop.example.com/api/packages/my-widget/versions/4/download/",
              "content_hash": "ccc333"
            }
            """, HttpStatusCode.Created);

        var zipData = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        var result = await _client.PublishVersionAsync("my-widget", zipData, "Fixed the frobnicator", "alice");

        result.IsSuccess.Should().BeTrue();

        var request = _messageHandler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/packages/my-widget/versions/"));

        // Quote characters are stripped because the framework only quotes
        // multipart names and file names when they are not valid tokens.
        var requestBody = _messageHandler.RequestBodies.Single().Replace("\"", "");
        requestBody.Should().Contain("name=file; filename=my-widget.zip");
        requestBody.Should().Contain("name=summary");
        requestBody.Should().Contain("Fixed the frobnicator");
        requestBody.Should().Contain("name=author");
        requestBody.Should().Contain("alice");

        var receipt = result.Value;
        receipt.PackageName.Should().Be("my-widget");
        receipt.Version.Should().Be(4);
        receipt.Author.Should().Be("alice");
        receipt.ContentHash.Should().Be("ccc333");
    }

    [Test]
    public async Task PublishVersion_WithoutSummary_OmitsSummaryPart()
    {
        _messageHandler.Responder = _ => JsonResponse("""
            { "package": "my-widget", "version": 1, "author": "alice", "download_url": "", "content_hash": "ddd444" }
            """, HttpStatusCode.Created);

        var result = await _client.PublishVersionAsync("my-widget", [0x50]);

        result.IsSuccess.Should().BeTrue();
        var requestBody = _messageHandler.RequestBodies.Single().Replace("\"", "");
        requestBody.Should().NotContain("name=summary");
        requestBody.Should().NotContain("name=author");
    }

    [Test]
    public async Task DownloadVersion_ReturnsZipBytes()
    {
        var zipData = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x42 };
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zipData)
        };

        var result = await _client.DownloadVersionAsync("my-widget", 2);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(zipData);

        var request = _messageHandler.Requests.Single();
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/packages/my-widget/versions/2/download/"));
    }

    [Test]
    public async Task DownloadVersion_DeletedVersion_Fails()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.Gone);

        var result = await _client.DownloadVersionAsync("my-widget", 1);

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("deleted");
    }

    [Test]
    public async Task DownloadLatest_NoLiveVersion_Fails()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var result = await _client.DownloadLatestAsync("my-widget");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("no live version");

        var request = _messageHandler.Requests.Single();
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/packages/my-widget/latest/"));
    }

    [Test]
    public async Task SetAlias_SendsPutWithVersionBody()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK);

        var result = await _client.SetAliasAsync("my-widget", "stable", 2);

        result.IsSuccess.Should().BeTrue();

        var request = _messageHandler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Put);
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/packages/my-widget/aliases/stable/"));
        _messageHandler.RequestBodies.Single().Should().Be("""{"version":2}""");
    }

    [Test]
    public async Task RemoveAlias_SendsDelete()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        var result = await _client.RemoveAliasAsync("my-widget", "stable");

        result.IsSuccess.Should().BeTrue();

        var request = _messageHandler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/packages/my-widget/aliases/stable/"));
    }

    [Test]
    public async Task DeleteVersion_SendsDeleteToVersionEndpoint()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        var result = await _client.DeleteVersionAsync("my-widget", 2);

        result.IsSuccess.Should().BeTrue();

        var request = _messageHandler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/packages/my-widget/versions/2/"));
    }

    [Test]
    public async Task DeleteVersion_NotFound_Fails()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var result = await _client.DeleteVersionAsync("my-widget", 9);

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("not found");
    }

    [Test]
    public async Task DeleteVersion_AlreadyDeleted_Fails()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.Gone);

        var result = await _client.DeleteVersionAsync("my-widget", 1);

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("already been deleted");
    }

    [Test]
    public async Task DeletePackage_SendsDeleteToPackageEndpoint()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        var result = await _client.DeletePackageAsync("my-widget");

        result.IsSuccess.Should().BeTrue();

        var request = _messageHandler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/packages/my-widget/"));
    }

    [Test]
    public async Task DeletePackage_NotFound_Fails()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var result = await _client.DeletePackageAsync("missing-package");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("missing-package");
        result.MessageChain.Should().Contain("not found");
    }

    [Test]
    public async Task GetVersionHistory_ReturnsPlainText()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Version 2 published by bob\nVersion 1 published by alice", Encoding.UTF8, "text/plain")
        };

        var result = await _client.GetVersionHistoryAsync("my-widget", 2);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Version 2 published by bob");

        var request = _messageHandler.Requests.Single();
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/packages/my-widget/versions/2/history/"));
    }

    [Test]
    public async Task Request_Unauthorized_FailsWithoutEchoingKey()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var result = await _client.ListPackagesAsync();

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("Application Key");
        result.MessageChain.Should().Contain("Settings");
        result.MessageChain.Should().NotContain(TestApplicationKey);
        result.DiagnosticReport.Should().NotContain(TestApplicationKey);
    }

    [Test]
    public async Task HttpUrl_NonLoopbackHost_RejectedBeforeSending()
    {
        SetStoredConnection("http://workshop.example.com", TestApplicationKey);

        var result = await _client.ListPackagesAsync();

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("HTTPS");
        result.MessageChain.Should().NotContain(TestApplicationKey);
        _messageHandler.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task HttpUrl_Localhost_Allowed()
    {
        SetStoredConnection("http://localhost:8000", TestApplicationKey);
        _messageHandler.Responder = _ => JsonResponse("[]");

        var result = await _client.ListPackagesAsync();

        result.IsSuccess.Should().BeTrue();
        _messageHandler.Requests.Single().RequestUri
            .Should().Be(new Uri("http://localhost:8000/api/packages/"));
    }

    [Test]
    public async Task BaseUrlWithPathSegment_KeepsThePathWhenBuildingEndpoints()
    {
        SetStoredConnection("https://example.com/workshop", TestApplicationKey);
        _messageHandler.Responder = _ => JsonResponse("[]");

        var result = await _client.ListPackagesAsync();

        result.IsSuccess.Should().BeTrue();
        _messageHandler.Requests.Single().RequestUri
            .Should().Be(new Uri("https://example.com/workshop/api/packages/"));
    }

    [Test]
    public async Task NoStoredConnection_FailsWithCredentialError()
    {
        _credentialService.GetWorkshopConnectionAsync()
            .Returns(Result<WorkshopConnection>.Fail("No Workshop connection is stored"));

        var result = await _client.ListPackagesAsync();

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("No Workshop connection is stored");
        _messageHandler.Requests.Should().BeEmpty();
    }

    private void SetStoredConnection(string workshopUrl, string applicationKey)
    {
        var connection = new WorkshopConnection(workshopUrl, applicationKey);
        _credentialService.GetWorkshopConnectionAsync()
            .Returns(Result<WorkshopConnection>.Ok(connection));
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    // Records every request (and its body, captured before the request is
    // disposed) and serves the canned response from Responder.
    private sealed class StubMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();

        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(string.Empty)
            };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            var body = string.Empty;
            if (request.Content is not null)
            {
                body = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            RequestBodies.Add(body);

            return Responder(request);
        }
    }
}
