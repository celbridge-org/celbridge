using System.Net.Http;

namespace Celbridge.WebHost.Services;

/// <summary>
/// Fetches a page's favicon file, and keeps the address and the image of the last icon fetched. The page chooses the
/// address, so the reader fetches only http and https addresses and limits the file's size. The reader also returns
/// only PNG, JPEG, GIF, WebP, BMP and ICO images. The tab bar can draw those formats. The caller supplies a renderer
/// that turns an SVG file into a PNG.
/// </summary>
internal sealed class WebViewFaviconReader
{
    // A favicon of any real size fits well inside this.
    internal const int MaximumFaviconBytes = 1024 * 1024;

    private const string SvgMediaType = "image/svg+xml";

    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient HttpClient = new();

    // The address and the image of the last icon fetched. Empty and null until a fetch succeeds.
    private string _lastAddress = string.Empty;
    private byte[]? _lastImage;

    // True from a call to RefreshNextRead until the next read.
    private bool _refreshNextRead;

    /// <summary>
    /// Returns true for an address the fetch accepts.
    /// </summary>
    public static bool IsAllowedAddress(string address, out Uri uri)
    {
        if (Uri.TryCreate(address, UriKind.Absolute, out var parsedUri)
            && (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsedUri;
            return true;
        }

        uri = null!;
        return false;
    }

    /// <summary>
    /// Returns true when the bytes begin with the signature of PNG, JPEG, GIF, WebP, BMP or ICO. SVG and a server's
    /// HTML error page both fail this check.
    /// </summary>
    public static bool IsSupportedImage(ReadOnlySpan<byte> bytes)
    {
        return bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 })
            || bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })
            || bytes.StartsWith("GIF8"u8)
            || (bytes.StartsWith("RIFF"u8) && bytes.Length >= 12 && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
            || bytes.StartsWith("BM"u8)
            || bytes.StartsWith(new byte[] { 0x00, 0x00, 0x01, 0x00 });
    }

    /// <summary>
    /// Makes the next read fetch the icon, even at the last icon's address.
    /// </summary>
    public void RefreshNextRead()
    {
        _refreshNextRead = true;
    }

    /// <summary>
    /// Returns the image at the address. When the address is the last icon's address, the reader returns the last
    /// image without fetching. The reader fetches the image at any other address. After a call to RefreshNextRead,
    /// the reader always fetches. If that fetch fails, the reader returns the last image.
    /// </summary>
    public async Task<byte[]?> ReadAsync(string address, Func<string, Task<byte[]?>> fetch)
    {
        var refresh = _refreshNextRead;
        _refreshNextRead = false;

        var last = address == _lastAddress ? _lastImage : null;
        if (last is not null
            && !refresh)
        {
            return last;
        }

        var fetched = await fetch(address);
        if (fetched is null)
        {
            return last;
        }

        _lastAddress = address;
        _lastImage = fetched;

        return fetched;
    }

    /// <summary>
    /// Fetches the image at the address. The request names the given user agent, because some sites refuse a request
    /// with none. Returns null for an address the fetch refuses, a failed request, a file over the size limit and an
    /// unsupported format. Also returns null for a fetch still unfinished after ten seconds.
    /// </summary>
    public static async Task<byte[]?> FetchAsync(string address, string userAgent, Func<byte[], Task<byte[]?>> renderSvg)
    {
        return await FetchAsync(address, userAgent, renderSvg, FetchTimeout);
    }

    /// <summary>
    /// Fetches the image at the address, with a limit on the whole fetch, body included.
    /// </summary>
    internal static async Task<byte[]?> FetchAsync(
        string address,
        string userAgent,
        Func<byte[], Task<byte[]?>> renderSvg,
        TimeSpan timeout)
    {
        if (!IsAllowedAddress(address, out var uri))
        {
            return null;
        }

        using var cancellation = new CancellationTokenSource(timeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (userAgent.Length > 0)
            {
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            }

            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            if (response.Content.Headers.ContentLength > MaximumFaviconBytes)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token);
            var bytes = await ReadWithLimitAsync(stream, cancellation.Token);
            if (bytes is null)
            {
                return null;
            }

            if (IsSupportedImage(bytes))
            {
                return bytes;
            }

            var isSvg = string.Equals(response.Content.Headers.ContentType?.MediaType, SvgMediaType, StringComparison.OrdinalIgnoreCase);
            if (!isSvg)
            {
                return null;
            }

            return await renderSvg(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> ReadWithLimitAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaximumFaviconBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }
    }
}
