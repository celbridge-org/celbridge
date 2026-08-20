using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the focus notification discriminator. Every message a page sends its host arrives on the
/// same web message event as these notifications, so the discriminator has to separate the two without acting
/// on page content that merely mentions a method name.
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
        WebViewFocusRegistry.ReadFocusNotification(WrappedNotification)?.Method.Should().Be("input/focusLost");
    }

    [Test]
    public void BareNotification_IsRecognized()
    {
        WebViewFocusRegistry.ReadFocusNotification(BareNotification)?.Method.Should().Be("input/focusLost");
    }

    [Test]
    public void MethodNameInsideContent_IsNotRecognized()
    {
        // Editor content reaches the host over the same channel, so a document that happens to contain the
        // method name must not release the surface the user is typing into.
        var contentNotification =
            """{"jsonrpc":"2.0","method":"document/setContent","params":{"text":"input/focusLost"}}""";

        WebViewFocusRegistry.ReadFocusNotification(contentNotification).Should().BeNull();
    }

    [Test]
    public void AnotherMethod_IsNotRecognized()
    {
        var otherNotification =
            """{"jsonrpc":"2.0","method":"input/linkClicked","params":{"href":"input/focusLost"}}""";

        WebViewFocusRegistry.ReadFocusNotification(otherNotification).Should().BeNull();
    }

    [Test]
    public void GainAndRecoveredBlur_AreRecognizedWithTheirPage()
    {
        var gained =
            """{"jsonrpc":"2.0","method":"input/focusGained","params":{"path":"/celbridge-code-editor/index.html"}}""";
        var retained =
            """{"jsonrpc":"2.0","method":"input/focusRetained","params":{"path":"/celbridge-console/index.html"}}""";

        var gainedNotification = WebViewFocusRegistry.ReadFocusNotification(gained);
        gainedNotification!.Method.Should().Be("input/focusGained");
        gainedNotification.Path.Should().Be("/celbridge-code-editor/index.html");

        var retainedNotification = WebViewFocusRegistry.ReadFocusNotification(retained);
        retainedNotification!.Method.Should().Be("input/focusRetained");
        retainedNotification.Path.Should().Be("/celbridge-console/index.html");
    }

    [Test]
    public void NotificationWithoutParams_IsRecognizedWithNoPage()
    {
        var notification = WebViewFocusRegistry.ReadFocusNotification(BareNotification);

        notification!.Method.Should().Be("input/focusLost");
        notification.Path.Should().BeNull();
    }

    [Test]
    public void MalformedMessage_IsNotRecognized()
    {
        WebViewFocusRegistry.ReadFocusNotification("input/focusLost").Should().BeNull();
        WebViewFocusRegistry.ReadFocusNotification("{\"method\":").Should().BeNull();
    }
}
