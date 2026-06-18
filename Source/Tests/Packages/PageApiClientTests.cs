using System.Net;
using System.Text;
using Celbridge.Credentials;
using Celbridge.Packages;
using Celbridge.Settings;

namespace Celbridge.Tests.Packages;

[TestFixture]
public class PageApiClientTests
{
    private const string TestWorkshopKey = "kpf_testkey_supersecret";

    private StubMessageHandler _messageHandler = null!;
    private ICredentialService _credentialService = null!;
    private IEditorSettings _editorSettings = null!;
    private PageApiClient _client = null!;

    [SetUp]
    public void Setup()
    {
        _messageHandler = new StubMessageHandler();
        _credentialService = Substitute.For<ICredentialService>();
        _editorSettings = Substitute.For<IEditorSettings>();
        SetStoredConnection("https://workshop.example.com", TestWorkshopKey);
        _client = new PageApiClient(_credentialService, _editorSettings, _messageHandler);
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
    }

    [Test]
    public async Task ListPages_SendsApiKeyHeaderAndParsesEntries()
    {
        _messageHandler.Responder = _ => JsonResponse("""
            [
              {
                "path": "my-site/home",
                "url": "https://workshop.example.com/pages/acme/my-site/home/",
                "published_at": "2026-02-01T09:30:00Z",
                "published_by": "alice",
                "content_hash": "aaa111"
              },
              {
                "path": "marketing/launch",
                "url": "https://workshop.example.com/pages/acme/marketing/launch/",
                "published_at": "2026-03-05T11:15:00Z",
                "published_by": "bob",
                "content_hash": "bbb222"
              }
            ]
            """);

        var result = await _client.ListPagesAsync();

        result.IsSuccess.Should().BeTrue();

        var request = _messageHandler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/pages"));
        request.Headers.Authorization.Should().NotBeNull();
        request.Headers.Authorization!.Scheme.Should().Be("Api-Key");
        request.Headers.Authorization.Parameter.Should().Be(TestWorkshopKey);

        var pages = result.Value;
        pages.Should().HaveCount(2);
        pages[0].Path.Should().Be("my-site/home");
        pages[0].Url.Should().Be("https://workshop.example.com/pages/acme/my-site/home/");
        pages[0].PublishedBy.Should().Be("alice");
        pages[0].ContentHash.Should().Be("aaa111");
        pages[1].Path.Should().Be("marketing/launch");
    }

    [Test]
    public async Task GetPage_MultiSegmentPath_BuildsEndpointAndParses()
    {
        _messageHandler.Responder = _ => JsonResponse("""
            {
              "path": "my-site/home",
              "url": "https://workshop.example.com/pages/acme/my-site/home/",
              "published_at": "2026-02-01T09:30:00Z",
              "published_by": "alice",
              "content_hash": "aaa111"
            }
            """);

        var result = await _client.GetPageAsync("my-site/home");

        result.IsSuccess.Should().BeTrue();

        var request = _messageHandler.Requests.Single();
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/pages/my-site/home"));

        var page = result.Value;
        page.Path.Should().Be("my-site/home");
        page.Url.Should().Be("https://workshop.example.com/pages/acme/my-site/home/");
    }

    [Test]
    public async Task GetPage_RelativeUrl_ResolvedAgainstWorkshopBase()
    {
        _messageHandler.Responder = _ => JsonResponse("""
            {
              "path": "my-site/home",
              "url": "/pages/acme/my-site/home/",
              "published_at": "2026-02-01T09:30:00Z",
              "published_by": "alice",
              "content_hash": "aaa111"
            }
            """);

        var result = await _client.GetPageAsync("my-site/home");

        result.IsSuccess.Should().BeTrue();
        result.Value.Url.Should().Be("https://workshop.example.com/pages/acme/my-site/home/");
    }

    [Test]
    public async Task GetPage_NotPublished_Fails()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var result = await _client.GetPageAsync("my-site/missing");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("my-site/missing");
        result.MessageChain.Should().Contain("No page is published");
    }

    [Test]
    public async Task PublishPage_SendsMultipartAndParsesPage()
    {
        _messageHandler.Responder = _ => JsonResponse("""
            {
              "path": "my-site/home",
              "url": "https://workshop.example.com/pages/acme/my-site/home/",
              "published_at": "2026-04-01T08:00:00Z",
              "published_by": "alice",
              "content_hash": "ccc333"
            }
            """, HttpStatusCode.Created);

        var zipData = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        var result = await _client.PublishPageAsync(zipData, "my-site/home", "alice");

        result.IsSuccess.Should().BeTrue();

        var request = _messageHandler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/pages"));

        var requestBody = _messageHandler.RequestBodies.Single().Replace("\"", "");
        requestBody.Should().Contain("name=file; filename=page.zip");
        requestBody.Should().Contain("name=path");
        requestBody.Should().Contain("my-site/home");
        requestBody.Should().Contain("name=author");
        requestBody.Should().Contain("alice");

        var page = result.Value;
        page.Path.Should().Be("my-site/home");
        page.Url.Should().Be("https://workshop.example.com/pages/acme/my-site/home/");
        page.ContentHash.Should().Be("ccc333");
    }

    [Test]
    public async Task PublishPage_PathOverlap_Fails()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.Conflict);

        var result = await _client.PublishPageAsync([0x50], "my-site/home");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("already published");
    }

    [Test]
    public async Task PublishPage_BadBundle_SurfacesServerMessage()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage((HttpStatusCode)422)
        {
            Content = new StringContent("pages.toml is missing [publish].path", Encoding.UTF8, "text/plain")
        };

        var result = await _client.PublishPageAsync([0x50], "my-site/home");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("422");
        result.MessageChain.Should().Contain("pages.toml is missing");
    }

    [Test]
    public async Task UnpublishPage_SendsDeleteToPathEndpoint()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NoContent);

        var result = await _client.UnpublishPageAsync("my-site/home");

        result.IsSuccess.Should().BeTrue();

        var request = _messageHandler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri.Should().Be(new Uri("https://workshop.example.com/api/pages/my-site/home"));
    }

    [Test]
    public async Task UnpublishPage_NotPublished_Fails()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        var result = await _client.UnpublishPageAsync("my-site/missing");

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("my-site/missing");
        result.MessageChain.Should().Contain("No page is published");
    }

    [Test]
    public async Task Request_Unauthorized_FailsWithoutEchoingKey()
    {
        _messageHandler.Responder = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var result = await _client.ListPagesAsync();

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("Workshop Key");
        result.MessageChain.Should().NotContain(TestWorkshopKey);
        result.DiagnosticReport.Should().NotContain(TestWorkshopKey);
    }

    [Test]
    public async Task HttpUrl_NonLoopbackHost_RejectedBeforeSending()
    {
        SetStoredConnection("http://workshop.example.com", TestWorkshopKey);

        var result = await _client.ListPagesAsync();

        result.IsFailure.Should().BeTrue();
        result.MessageChain.Should().Contain("HTTPS");
        _messageHandler.Requests.Should().BeEmpty();
    }

    // The Workshop URL is a non-secret setting; the Workshop Key is the only
    // value held in the credential store.
    private void SetStoredConnection(string workshopUrl, string workshopKey)
    {
        _editorSettings.WorkshopUrl.Returns(workshopUrl);
        _credentialService.GetWorkshopKeyAsync()
            .Returns(Result<string>.Ok(workshopKey));
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
