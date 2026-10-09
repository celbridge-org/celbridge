using Celbridge.Host;
using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the focus-lost script. It builds its messages by hand, so the web channel's contract tests do not
/// see its method names, and these tests hold them to the host's.
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
