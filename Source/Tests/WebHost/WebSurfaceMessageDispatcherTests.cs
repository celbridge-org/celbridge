using Celbridge.Tests.Helpers;
using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the dispatcher that routes a page's notifications from the native web message bus to the
/// handler for each method.
/// </summary>
[TestFixture]
public class WebSurfaceMessageDispatcherTests
{
    private const string LogMethod = "host/log";

    private const string LogNotification =
        """{"jsonrpc":"2.0","method":"host/log","params":{"level":"debug","message":"hello"}}""";

    private WebSurfaceMessageDispatcher _dispatcher = null!;
    private List<WebSurfaceMessage> _received = null!;

    [SetUp]
    public void SetUp()
    {
        _dispatcher = new WebSurfaceMessageDispatcher(new NullLogger<WebSurfaceMessageDispatcher>());
        _received = new List<WebSurfaceMessage>();
        _dispatcher.AddHandler(LogMethod, _received.Add);
    }

    [Test]
    public void ObservedView_ItsNotificationReachesTheHandlerWithTheView()
    {
        var view = new FakeWebView("notes.md");
        _dispatcher.Observe(view);

        view.PostMessage(LogNotification);

        _received.Should().ContainSingle();
        _received[0].View.Should().BeSameAs(view);
        WebMessageEnvelope.ReadString(_received[0].Parameters, "message").Should().Be("hello");
    }

    [Test]
    public void ObservedView_NotificationForAnotherMethod_ReachesNoHandler()
    {
        var view = new FakeWebView("notes.md");
        _dispatcher.Observe(view);

        view.PostMessage("""{"jsonrpc":"2.0","method":"input/focusLost"}""");

        _received.Should().BeEmpty();
    }

    [Test]
    public void ViewNotObserved_ItsNotificationReachesNoHandler()
    {
        var view = new FakeWebView("notes.md");

        view.PostMessage(LogNotification);

        _received.Should().BeEmpty();
    }
}
