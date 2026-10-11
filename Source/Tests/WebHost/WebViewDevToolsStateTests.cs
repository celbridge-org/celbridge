using Celbridge.Downloads;
using Celbridge.Localization;
using Celbridge.Tests.Helpers;
using Celbridge.UserInterface;
using Celbridge.WebHost.Platform;

namespace Celbridge.Tests.WebHost;

[TestFixture]
public class WebViewDevToolsStateTests
{
    private RecordingLogger<SkiaWebViewPlatform> _logger = null!;
    private SkiaWebViewPlatform _platform = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new RecordingLogger<SkiaWebViewPlatform>();
        _platform = new SkiaWebViewPlatform(
            _logger,
            new NullLogger<SkiaWebView>(),
            new NullLogger<MacOSWebView>(),
            new NullLogger<MacOSWebViewDownloadRouter>(),
            Substitute.For<IUserInterfaceService>(),
            Substitute.For<ILocalizerService>(),
            Substitute.For<IDownloadService>());
    }

    [Test]
    public void ReportRemoteInspectionOnce_CalledForEveryWebView_ReportsOnlyTheFirst()
    {
        _platform.ReportRemoteInspectionOnce(enabled: true, applied: true);
        _platform.ReportRemoteInspectionOnce(enabled: true, applied: true);
        _platform.ReportRemoteInspectionOnce(enabled: false, applied: true);

        _logger.EntriesAt(LogEntryLevel.Debug).Should().HaveCount(1);
        _logger.EntriesAt(LogEntryLevel.Warning).Should().BeEmpty();
    }

    [Test]
    public void ReportRemoteInspectionOnce_SettingNotAccepted_WarnsThatPagesCannotBeInspected()
    {
        _platform.ReportRemoteInspectionOnce(enabled: true, applied: false);

        _logger.EntriesAt(LogEntryLevel.Warning).Should().HaveCount(1);
        _logger.EntriesAt(LogEntryLevel.Debug).Should().BeEmpty();
    }
}
