using Celbridge.Host;
using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the focus-lost script. The script builds its messages by hand, so the web channel's contract tests
/// never see its method names. These tests check that the script's method names match the host's.
/// </summary>
[TestFixture]
public class WebViewFocusLostScriptTests
{
    [Test]
    public void Source_ReportsALossUnderTheHostsMethodName()
    {
        WebViewFocusLostScript.Source.Should().Contain($"method: '{InputRpcMethods.FocusLost}'");
    }

    [Test]
    public void Source_LogsUnderTheHostsMethodName()
    {
        WebViewFocusLostScript.Source.Should().Contain($"method: '{LogRpcMethods.Log}'");
    }
}
