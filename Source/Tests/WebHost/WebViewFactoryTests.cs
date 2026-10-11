using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the web view factory. The platform here creates views with no control, so each view is ready at
/// once and the prewarm queue fills as the factory is built.
/// </summary>
[TestFixture]
public class WebViewFactoryTests
{
    [Test]
    public async Task AcquireAsync_DisposesAViewThatFailsToConfigure()
    {
        var created = new List<ControlFreeWebView>();
        var platform = Substitute.For<IWebViewPlatform>();
        platform.CreateWebViewAsync().Returns(_ =>
        {
            var view = new ControlFreeWebView(platform, new NullLogger<ControlFreeWebView>());
            view.ApplyingOptions = _ => throw new InvalidOperationException("The browser process has gone");
            created.Add(view);
            return Task.FromResult<WebViewBase>(view);
        });

        var factory = new WebViewFactory(
            new NullLogger<WebViewFactory>(),
            platform,
            new WebSurfaceMessageDispatcher(new NullLogger<WebSurfaceMessageDispatcher>()),
            Substitute.For<IFeatureFlags>());

        var acquire = async () => await factory.AcquireAsync(WebViewOptions.Default);
        await acquire.Should().ThrowAsync<InvalidOperationException>();

        created[0].IsDisposed.Should().BeTrue();

        factory.Shutdown();
    }
}
