using Celbridge.Tests.Helpers;
using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for a call that is still in flight when its view closes. A WebView2 that closes can drop that call, and
/// the call then never completes. So the view fails the call when the view closes.
/// </summary>
[TestFixture]
public class WebViewPendingCallTests
{
    private static readonly TimeSpan Backstop = TimeSpan.FromSeconds(5);

    [Test]
    public async Task EvalAsync_InFlightWhenTheViewCloses_FailsAsClosed()
    {
        var view = CreateView();
        view.Evaluate = _ => new TaskCompletionSource<string>().Task;

        var evaluation = view.EvalAsync("1 + 1");
        evaluation.IsCompleted.Should().BeFalse();

        view.Dispose();

        var awaiting = async () => await evaluation.WaitAsync(Backstop);
        await awaiting.Should().ThrowAsync<ObjectDisposedException>().WithMessage("*has closed*");
    }

    [Test]
    public async Task EvalAsync_AnsweredBeforeTheViewCloses_KeepsItsValue()
    {
        var page = new TaskCompletionSource<string>();
        var view = CreateView();
        view.Evaluate = _ => page.Task;

        var evaluation = view.EvalAsync("1 + 1");
        page.SetResult("2");
        view.Dispose();

        (await evaluation.WaitAsync(Backstop)).Should().Be("2");
    }

    private static ControlFreeWebView CreateView()
    {
        return new ControlFreeWebView(Substitute.For<IWebViewPlatform>(), new NullLogger<ControlFreeWebView>());
    }
}
