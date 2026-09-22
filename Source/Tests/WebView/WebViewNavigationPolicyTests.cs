using Celbridge.Commands;
using Celbridge.UserInterface;
using Celbridge.WebHost;
using Celbridge.WebView.Services;

namespace Celbridge.Tests.WebView;

[TestFixture]
public class WebViewNavigationPolicyTests
{
    private ICommandService _commandService = null!;
    private WebViewNavigationPolicy _policy = null!;

    [SetUp]
    public void SetUp()
    {
        _commandService = Substitute.For<ICommandService>();
        var logger = Substitute.For<ILogger<WebViewNavigationPolicy>>();
        _policy = new WebViewNavigationPolicy(_commandService, Substitute.For<IWebViewAdapter>(), logger);
    }

    [Test]
    public void ANavigationTheHandlerAllows_GoesAhead()
    {
        var isAllowed = _policy.Decide(Request(), _ => Task.FromResult(NavigationDecision.Allow));

        isAllowed.Should().BeTrue();
        _commandService.DidNotReceiveWithAnyArgs().Execute<IOpenBrowserCommand>();
    }

    [Test]
    public void ANavigationTheHandlerSendsToTheSystemBrowser_IsRefused_AndOpensTheBrowser()
    {
        var isAllowed = _policy.Decide(Request(), _ => Task.FromResult(NavigationDecision.OpenInSystemBrowser));

        isAllowed.Should().BeFalse();
        _commandService.ReceivedWithAnyArgs(1).Execute<IOpenBrowserCommand>();
    }

    [Test]
    public void ANavigationStillBeingDecided_IsRefusedAtOnce_AndActedOnOnceDecided()
    {
        var decision = new TaskCompletionSource<NavigationDecision>();

        var isAllowed = _policy.Decide(Request(), _ => decision.Task);

        // Refused before the handler answers, so no request goes out while the user is being asked.
        isAllowed.Should().BeFalse();
        _commandService.DidNotReceiveWithAnyArgs().Execute<IOpenBrowserCommand>();

        decision.SetResult(NavigationDecision.OpenInSystemBrowser);

        _commandService.ReceivedWithAnyArgs(1).Execute<IOpenBrowserCommand>();
    }

    [Test]
    public void OpenInSystemBrowserDecision_InvokesOpenBrowserCommand_WithDestinationUrl()
    {
        var destination = new Uri("https://example.com/page");

        _policy.DispatchSideEffect(NavigationDecision.OpenInSystemBrowser, destination);

        _commandService.ReceivedWithAnyArgs(1).Execute<IOpenBrowserCommand>();
    }

    [Test]
    public void OpenInSystemBrowserDecision_PassesDestinationToCommand()
    {
        var destination = new Uri("https://example.com/path?q=1");
        var capturedCommand = Substitute.For<IOpenBrowserCommand>();

        _commandService.WhenForAnyArgs(c => c.Execute<IOpenBrowserCommand>())
            .Do(callInfo =>
            {
                var configurator = callInfo.Arg<Action<IOpenBrowserCommand>>();
                configurator(capturedCommand);
            });

        _policy.DispatchSideEffect(NavigationDecision.OpenInSystemBrowser, destination);

        capturedCommand.Received().URL = destination.ToString();
    }

    [Test]
    public void CancelDecision_DoesNotInvokeOpenBrowserCommand()
    {
        var destination = new Uri("https://example.com/page");

        _policy.DispatchSideEffect(NavigationDecision.Cancel, destination);

        _commandService.DidNotReceiveWithAnyArgs().Execute<IOpenBrowserCommand>();
    }

    [Test]
    public void AllowDecision_DoesNotInvokeOpenBrowserCommand()
    {
        var destination = new Uri("https://example.com/page");

        _policy.DispatchSideEffect(NavigationDecision.Allow, destination);

        _commandService.DidNotReceiveWithAnyArgs().Execute<IOpenBrowserCommand>();
    }

    private static NavigationRequest Request()
    {
        return new NavigationRequest(new Uri("https://example.com/elsewhere"), IsUserInitiated: true);
    }
}
