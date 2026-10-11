using Celbridge.Settings;
using Celbridge.Tests.Helpers;
using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for how a view installs a script that runs at document start.
/// </summary>
[TestFixture]
public class WebViewDocumentStartScriptTests
{
    [Test]
    public async Task AddDocumentStartScriptAsync_RunsAScriptThatFailsToInstallAfterEachNavigationInstead()
    {
        var logger = new RecordingLogger<ControlFreeWebView>();
        var view = new ControlFreeWebView(Substitute.For<IWebViewPlatform>(), logger);
        await view.ConfigureAsync(WebViewOptions.Default, Substitute.For<IFeatureFlags>());
        view.InstallingScript = _ => throw new InvalidOperationException("The CoreWebView2 has closed");

        var add = async () => await view.AddDocumentStartScriptAsync("window.started = true;");
        await add.Should().NotThrowAsync();

        logger.EntriesAt(LogEntryLevel.Warning)
            .Should().ContainSingle(entry => entry.Message!.Contains("runs after each navigation instead"));

        view.Dispose();
    }
}
