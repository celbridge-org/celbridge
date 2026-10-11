using Celbridge.WebHost;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for how a web view reports a navigation's outcome from WebView2's status.
/// </summary>
[TestFixture]
public class WebNavigationResultTests
{
    [Test]
    public void ASuccessfulNavigation_SucceedsWithNoErrorStatus()
    {
        var completion = WebViewBase.CreateNavigationCompletedEventArgs(true, CoreWebView2WebErrorStatus.Unknown);

        completion.Result.Should().Be(WebNavigationResult.Succeeded);
        completion.ErrorStatus.Should().BeEmpty();
    }

    [TestCase(CoreWebView2WebErrorStatus.OperationCanceled, WebNavigationResult.Cancelled)]
    [TestCase(CoreWebView2WebErrorStatus.ConnectionAborted, WebNavigationResult.Aborted)]
    [TestCase(CoreWebView2WebErrorStatus.HostNameNotResolved, WebNavigationResult.Failed)]
    public void AFailedNavigation_ReportsItsResultAndNamesItsStatus(
        CoreWebView2WebErrorStatus status,
        WebNavigationResult expectedResult)
    {
        var completion = WebViewBase.CreateNavigationCompletedEventArgs(false, status);

        completion.Result.Should().Be(expectedResult);
        completion.ErrorStatus.Should().Be(status.ToString());
    }
}
