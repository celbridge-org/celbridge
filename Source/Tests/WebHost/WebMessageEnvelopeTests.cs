using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the reader that picks a page's notifications out of the native web message bus. Every
/// message a page sends its host arrives on the same event, so the reader has to separate a notification from
/// content that merely mentions a method name.
/// </summary>
[TestFixture]
public class WebMessageEnvelopeTests
{
    private const string FocusLostMethod = "input/focusLost";
    private const string LogMethod = "host/log";

    // The envelope as the WebView2 heads deliver it: the page posts a JS string, so the message arrives
    // wrapped in a JSON string literal.
    private const string WrappedNotification =
        "\"{\\\"jsonrpc\\\":\\\"2.0\\\",\\\"method\\\":\\\"input/focusLost\\\"}\"";

    // The same envelope as the macOS WKWebView head delivers it, unwrapped.
    private const string BareNotification =
        """{"jsonrpc":"2.0","method":"input/focusLost"}""";

    [Test]
    public void WrappedNotification_IsRecognized()
    {
        var notification = WebMessageEnvelope.TryRead(WrappedNotification, FocusLostMethod);

        notification!.Method.Should().Be(FocusLostMethod);
    }

    [Test]
    public void BareNotification_IsRecognized()
    {
        var notification = WebMessageEnvelope.TryRead(BareNotification, FocusLostMethod);

        notification!.Method.Should().Be(FocusLostMethod);
    }

    [Test]
    public void MethodNameInsideContent_IsNotRecognized()
    {
        // Editor content reaches the host over the same channel, so a document that happens to contain the
        // method name must not release the surface the user is typing into.
        var contentNotification =
            """{"jsonrpc":"2.0","method":"document/setContent","params":{"text":"input/focusLost"}}""";

        WebMessageEnvelope.TryRead(contentNotification, FocusLostMethod).Should().BeNull();
    }

    [Test]
    public void MethodNotAskedFor_IsNotRecognized()
    {
        WebMessageEnvelope.TryRead(BareNotification, LogMethod).Should().BeNull();
    }

    [Test]
    public void MalformedMessage_IsNotRecognized()
    {
        WebMessageEnvelope.TryRead("input/focusLost", FocusLostMethod).Should().BeNull();
        WebMessageEnvelope.TryRead("{\"method\":", FocusLostMethod).Should().BeNull();
    }

    [Test]
    public void Parameters_AreReadableAfterTheDocumentIsDisposed()
    {
        var logNotification =
            """{"jsonrpc":"2.0","method":"host/log","params":{"level":"error","message":"import failed"}}""";

        var notification = WebMessageEnvelope.TryRead(logNotification, FocusLostMethod, LogMethod);

        notification!.Method.Should().Be(LogMethod);
        WebMessageEnvelope.ReadString(notification.Parameters, "level").Should().Be("error");
        WebMessageEnvelope.ReadString(notification.Parameters, "message").Should().Be("import failed");
    }

    [Test]
    public void MissingParameter_ReadsAsNull()
    {
        var notification = WebMessageEnvelope.TryRead(BareNotification, FocusLostMethod);

        WebMessageEnvelope.ReadString(notification!.Parameters, "message").Should().BeNull();
    }
}
