using System.Net;
using System.Net.Sockets;
using System.Text;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;

namespace Celbridge.Tests.WebHost;

[TestFixture]
public class WebViewFaviconReaderTests
{
    [TestCase("https://example.com/favicon.ico", true)]
    [TestCase("http://127.0.0.1:8000/i.png", true)]
    [TestCase("file:///etc/passwd", false)]
    [TestCase("data:image/png;base64,AAAA", false)]
    [TestCase("about:blank", false)]
    [TestCase("not an address", false)]
    public void IsAllowedAddress_AcceptsOnlyHttpAndHttps(string address, bool expected)
    {
        WebViewFaviconReader.IsAllowedAddress(address, out _).Should().Be(expected);
    }

    [TestCase(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D }, true)]
    [TestCase(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [TestCase(new byte[] { 0x00, 0x00, 0x01, 0x00, 0x01 }, true)]
    [TestCase(new byte[] { }, false)]
    [TestCase(new byte[] { 0x00, 0x00, 0x02, 0x00 }, false)]
    public void IsSupportedImage_ChecksTheSignature(byte[] bytes, bool expected)
    {
        WebViewFaviconReader.IsSupportedImage(bytes).Should().Be(expected);
    }

    [TestCase("GIF89a", true)]
    [TestCase("BM6", true)]
    [TestCase("RIFF\0\0\0\0WEBPVP8", true)]
    [TestCase("RIFF\0\0\0\0WAVEfmt", false)]
    [TestCase("<svg xmlns=\"http://www.w3.org/2000/svg\"/>", false)]
    [TestCase("<!doctype html><title>Not found</title>", false)]
    public void IsSupportedImage_ChecksATextSignature(string text, bool expected)
    {
        WebViewFaviconReader.IsSupportedImage(Encoding.ASCII.GetBytes(text)).Should().Be(expected);
    }

    private const string IconAddress = "https://example.com/favicon.ico";

    private sealed class FakeFaviconStore : IWebViewFaviconStore
    {
        public Dictionary<string, byte[]> Files { get; } = new();

        public Task<byte[]?> TryReadIconAsync(string iconAddress)
        {
            return Task.FromResult(Files.GetValueOrDefault(iconAddress));
        }

        public Task WriteIconAsync(string iconAddress, byte[] favicon)
        {
            Files[iconAddress] = favicon;
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task ReadAsync_AStoredAddress_UsesTheStoredFileWithNoFetch()
    {
        var store = new FakeFaviconStore();
        store.Files[IconAddress] = new byte[] { 1 };
        var fetches = 0;

        var favicon = await WebViewFaviconReader.ReadAsync(IconAddress, store, refresh: false, address =>
        {
            fetches++;
            return Task.FromResult<byte[]?>(new byte[] { 2 });
        });

        favicon.Should().Equal(1);
        fetches.Should().Be(0);
    }

    [Test]
    public async Task ReadAsync_AnUnknownAddress_FetchesAndStoresTheFile()
    {
        var store = new FakeFaviconStore();

        var favicon = await WebViewFaviconReader.ReadAsync(IconAddress, store, refresh: false,
            address => Task.FromResult<byte[]?>(new byte[] { 2 }));

        favicon.Should().Equal(2);
        store.Files[IconAddress].Should().Equal(2);
    }

    [Test]
    public async Task ReadAsync_AFetchThatFails_StoresNothing()
    {
        var store = new FakeFaviconStore();

        var favicon = await WebViewFaviconReader.ReadAsync(IconAddress, store, refresh: false,
            address => Task.FromResult<byte[]?>(null));

        favicon.Should().BeNull();
        store.Files.Should().BeEmpty();
    }

    [Test]
    public async Task ReadAsync_ARefresh_FetchesAndReplacesTheStoredFile()
    {
        var store = new FakeFaviconStore();
        store.Files[IconAddress] = new byte[] { 1 };

        var favicon = await WebViewFaviconReader.ReadAsync(IconAddress, store, refresh: true,
            address => Task.FromResult<byte[]?>(new byte[] { 2 }));

        favicon.Should().Equal(2);
        store.Files[IconAddress].Should().Equal(2);
    }

    [Test]
    public async Task ReadAsync_ARefreshThatFails_KeepsTheStoredFile()
    {
        var store = new FakeFaviconStore();
        store.Files[IconAddress] = new byte[] { 1 };

        var favicon = await WebViewFaviconReader.ReadAsync(IconAddress, store, refresh: true,
            address => Task.FromResult<byte[]?>(null));

        favicon.Should().Equal(1);
        store.Files[IconAddress].Should().Equal(1);
    }

    private const string UserAgent = "Test Agent/1.0 (Mac) Celbridge/1.0";

    private static readonly byte[] RenderedPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D };

    private static Task<byte[]?> FailIfDrawn(byte[] file)
    {
        throw new AssertionException("Only an SVG file is drawn");
    }

    private static byte[] Response(string contentType, string body)
    {
        return Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
    }

    // Starts a server that answers one request with these bytes and then keeps the connection open. The returned task
    // finishes with the text of that request.
    private static (TcpListener Listener, string Address, Task<string> Request) StartServer(byte[] response)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var request = new TaskCompletionSource<string>();

        _ = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var length = await stream.ReadAsync(buffer);
            request.TrySetResult(Encoding.ASCII.GetString(buffer, 0, length));
            await stream.WriteAsync(response);
            await stream.FlushAsync();
            await Task.Delay(TimeSpan.FromSeconds(30));
        });

        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        return (listener, $"http://127.0.0.1:{port}/favicon.ico", request.Task);
    }

    [Test]
    public async Task FetchAsync_NamesTheUserAgent()
    {
        var (listener, address, request) = StartServer(Response("image/png", "\x89PNG"));
        using (listener)
        {
            await WebViewFaviconReader.FetchAsync(address, UserAgent, FailIfDrawn);

            (await request).Should().Contain($"User-Agent: {UserAgent}\r\n");
        }
    }

    [Test]
    public async Task FetchAsync_AnSvgFile_ReturnsWhatTheRendererDraws()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>";
        var (listener, address, _) = StartServer(Response("image/svg+xml; charset=utf-8", svg));
        using (listener)
        {
            byte[]? drawn = null;

            var favicon = await WebViewFaviconReader.FetchAsync(address, UserAgent, file =>
            {
                drawn = file;
                return Task.FromResult<byte[]?>(RenderedPng);
            });

            favicon.Should().Equal(RenderedPng);
            Encoding.ASCII.GetString(drawn!).Should().Be(svg);
        }
    }

    [Test]
    public async Task FetchAsync_AnSvgFileTheRendererCannotDraw_ReturnsNull()
    {
        var (listener, address, _) = StartServer(Response("image/svg+xml", "not really an image"));
        using (listener)
        {
            var favicon = await WebViewFaviconReader.FetchAsync(address, UserAgent, file => Task.FromResult<byte[]?>(null));

            favicon.Should().BeNull();
        }
    }

    [Test]
    public async Task FetchAsync_AnHtmlPage_IsNotDrawn()
    {
        var (listener, address, _) = StartServer(Response("text/html", "<!doctype html><svg></svg>"));
        using (listener)
        {
            var favicon = await WebViewFaviconReader.FetchAsync(address, UserAgent, FailIfDrawn);

            favicon.Should().BeNull();
        }
    }

    [Test]
    public async Task FetchAsync_ABodyThatStopsArriving_GivesUpAtTheTimeout()
    {
        // The headers arrive and the body stalls, so a limit on the headers alone would wait for the server.
        var headers = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: 1000\r\n\r\nabc");
        var (listener, address, _) = StartServer(headers);
        using (listener)
        {
            var read = WebViewFaviconReader.FetchAsync(address, UserAgent, FailIfDrawn, TimeSpan.FromMilliseconds(200));

            var finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(5)));

            finished.Should().BeSameAs(read);
            (await read).Should().BeNull();
        }
    }

    [Test]
    public async Task FetchAsync_AConnectionThatDrops_ReturnsNull()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using (listener)
        {
            _ = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                _ = await stream.ReadAsync(new byte[4096]);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1000\r\n\r\nabc"));
            });

            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            (await WebViewFaviconReader.FetchAsync($"http://127.0.0.1:{port}/favicon.ico", UserAgent, FailIfDrawn)).Should().BeNull();
        }
    }
}
