using Celbridge.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace Celbridge.Tests.Server;

/// <summary>
/// Tests for the file server's response to a request for a file. A media player needs the file's length, and seeks by
/// requesting a range of bytes.
/// </summary>
[TestFixture]
public class FileServerResponseTests
{
    private const string MediaPath = "media/clip.mp4";
    private const string ReaderOrigin = "https://reader.example.com";

    private static readonly byte[] FileBytes = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();

    private string _testFolder = null!;
    private PhysicalFileProvider _fileProvider = null!;
    private ServiceProvider _services = null!;
    private FileServer _fileServer = null!;

    [SetUp]
    public void Setup()
    {
        _testFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(FileServerResponseTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_testFolder, "media"));
        File.WriteAllBytes(Path.Combine(_testFolder, MediaPath), FileBytes);

        _fileProvider = new PhysicalFileProvider(_testFolder);
        _services = new ServiceCollection().AddLogging().BuildServiceProvider();
        _fileServer = new FileServer(Substitute.For<ILogger<FileServer>>());
        _fileServer.RegisterCrossOriginReader(ReaderOrigin);
    }

    [TearDown]
    public void TearDown()
    {
        _fileServer.Dispose();
        _services.Dispose();
        _fileProvider.Dispose();

        if (Directory.Exists(_testFolder))
        {
            Directory.Delete(_testFolder, recursive: true);
        }
    }

    [Test]
    public async Task ServeFileAsync_WholeFile_SendsTheFileWithItsLength()
    {
        var (response, body) = await ServeAsync(MediaPath);

        response.StatusCode.Should().Be(StatusCodes.Status200OK);
        response.ContentType.Should().Be("video/mp4");
        response.ContentLength.Should().Be(FileBytes.Length);
        body.Should().Equal(FileBytes);
    }

    [TestCase(null)]
    [TestCase("bytes=0-1")]
    public async Task ServeFileAsync_ForbidsCaching(string? range)
    {
        var (response, _) = await ServeAsync(MediaPath, range: range);

        response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Test]
    public async Task ServeFileAsync_WholeFile_AdvertisesRangeRequests()
    {
        var (response, _) = await ServeAsync(MediaPath);

        response.Headers.AcceptRanges.ToString().Should().Be("bytes");
    }

    [TestCase("bytes=0-1", 0, 1)]
    [TestCase("bytes=60-", 60, 63)]
    [TestCase("bytes=-4", 60, 63)]
    public async Task ServeFileAsync_RangeRequest_SendsThePartOfTheFile(string range, int first, int last)
    {
        var (response, body) = await ServeAsync(MediaPath, range: range);

        response.StatusCode.Should().Be(StatusCodes.Status206PartialContent);
        response.Headers.ContentRange.ToString().Should().Be($"bytes {first}-{last}/{FileBytes.Length}");
        response.ContentLength.Should().Be(last - first + 1);
        body.Should().Equal(FileBytes[first..(last + 1)]);
    }

    [Test]
    public async Task ServeFileAsync_RangePastTheEnd_IsRefused()
    {
        var (response, body) = await ServeAsync(MediaPath, range: "bytes=64-");

        response.StatusCode.Should().Be(StatusCodes.Status416RangeNotSatisfiable);
        response.Headers.ContentRange.ToString().Should().Be($"bytes */{FileBytes.Length}");
        body.Should().BeEmpty();
    }

    [Test]
    public async Task ServeFileAsync_RangeRequest_SendsTheCrossOriginPolicyHeaders()
    {
        var (response, _) = await ServeAsync(MediaPath, range: "bytes=0-1");

        response.Headers["Cross-Origin-Embedder-Policy"].ToString().Should().Be("credentialless");
        response.Headers["Cross-Origin-Opener-Policy"].ToString().Should().Be("same-origin");
    }

    [Test]
    public async Task ServeFileAsync_RegisteredOrigin_IsGrantedTheRead()
    {
        var (response, _) = await ServeAsync(MediaPath, range: "bytes=0-1", origin: ReaderOrigin, allowCrossOrigin: true);

        response.Headers.AccessControlAllowOrigin.ToString().Should().Be(ReaderOrigin);
    }

    [Test]
    public async Task ServeFileAsync_UnregisteredOrigin_IsNotGrantedTheRead()
    {
        var (response, _) = await ServeAsync(MediaPath, origin: "https://other.example.com", allowCrossOrigin: true);

        response.Headers.ContainsKey("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Test]
    public async Task ServeFileAsync_MissingFile_IsNotFound()
    {
        var (response, body) = await ServeAsync("media/missing.mp4");

        response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        body.Should().BeEmpty();
    }

    private async Task<(HttpResponse Response, byte[] Body)> ServeAsync(
        string path,
        string? range = null,
        string? origin = null,
        bool allowCrossOrigin = false)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = _services
        };
        context.Request.Method = HttpMethods.Get;
        if (range is not null)
        {
            context.Request.Headers.Range = range;
        }
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        var body = new MemoryStream();
        context.Response.Body = body;

        await _fileServer.ServeFileAsync(context, _fileProvider.GetFileInfo(path), path, allowCrossOrigin);

        return (context.Response, body.ToArray());
    }
}
