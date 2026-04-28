using Celbridge.Commands;
using Celbridge.Explorer;
using Celbridge.WebView.Services;

namespace Celbridge.Tests.Documents;

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
        _policy = new WebViewNavigationPolicy(_commandService, logger);
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

}
