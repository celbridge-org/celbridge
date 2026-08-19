using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the focus-lost discriminator. Every message a page sends its host arrives on the same web
/// message event as this notification, so the discriminator has to separate the two without acting on page
/// content that merely mentions the method name.
/// </summary>
[TestFixture]
public class FocusLostNotificationTests
{
    // The envelope the injected listener posts, as the WebView2 heads deliver it: the page posts a JS string,
    // so the message arrives wrapped in a JSON string literal.
    private const string WrappedNotification =
        "\"{\\\"jsonrpc\\\":\\\"2.0\\\",\\\"method\\\":\\\"input/focusLost\\\"}\"";

    // The same envelope as the macOS WKWebView head delivers it, unwrapped.
    private const string BareNotification =
        """{"jsonrpc":"2.0","method":"input/focusLost"}""";

    [Test]
    public void WrappedNotification_IsRecognized()
    {
        WebViewFocusRegistry.IsFocusLostNotification(WrappedNotification).Should().BeTrue();
    }

    [Test]
    public void BareNotification_IsRecognized()
    {
        WebViewFocusRegistry.IsFocusLostNotification(BareNotification).Should().BeTrue();
    }

    [Test]
    public void MethodNameInsideContent_IsNotRecognized()
    {
        // Editor content reaches the host over the same channel, so a document that happens to contain the
        // method name must not release the surface the user is typing into.
        var contentNotification =
            """{"jsonrpc":"2.0","method":"document/setContent","params":{"text":"input/focusLost"}}""";

        WebViewFocusRegistry.IsFocusLostNotification(contentNotification).Should().BeFalse();
    }

    [Test]
    public void AnotherMethod_IsNotRecognized()
    {
        var otherNotification =
            """{"jsonrpc":"2.0","method":"input/linkClicked","params":{"href":"input/focusLost"}}""";

        WebViewFocusRegistry.IsFocusLostNotification(otherNotification).Should().BeFalse();
    }

    [Test]
    public void MalformedMessage_IsNotRecognized()
    {
        WebViewFocusRegistry.IsFocusLostNotification("input/focusLost").Should().BeFalse();
        WebViewFocusRegistry.IsFocusLostNotification("{\"method\":").Should().BeFalse();
    }
}
