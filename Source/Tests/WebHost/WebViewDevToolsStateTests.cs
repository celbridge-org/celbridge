using Celbridge.Logging;
using Celbridge.WebHost.Platform;

namespace Celbridge.Tests.WebHost;

[TestFixture]
public class WebViewDevToolsStateTests
{
    private ILogger<SkiaWebViewAdapter> _logger = null!;
    private SkiaWebViewAdapter _adapter = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<SkiaWebViewAdapter>>();
        _adapter = new SkiaWebViewAdapter(_logger);
    }

    [Test]
    public void ReportRemoteInspectionOnce_CalledForEveryWebView_ReportsOnlyTheFirst()
    {
        _adapter.ReportRemoteInspectionOnce(enabled: true, applied: true);
        _adapter.ReportRemoteInspectionOnce(enabled: true, applied: true);
        _adapter.ReportRemoteInspectionOnce(enabled: false, applied: true);

        _logger.Received(1).LogDebug(Arg.Any<string?>(), Arg.Any<object?[]>());
        _logger.DidNotReceive().LogWarning(Arg.Any<string?>(), Arg.Any<object?[]>());
    }

    [Test]
    public void ReportRemoteInspectionOnce_SettingNotAccepted_WarnsThatPagesCannotBeInspected()
    {
        _adapter.ReportRemoteInspectionOnce(enabled: true, applied: false);

        _logger.Received(1).LogWarning(Arg.Any<string?>(), Arg.Any<object?[]>());
        _logger.DidNotReceive().LogDebug(Arg.Any<string?>(), Arg.Any<object?[]>());
    }
}
