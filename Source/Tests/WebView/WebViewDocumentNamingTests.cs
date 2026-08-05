using Celbridge.WebView.Commands;

namespace Celbridge.Tests.WebView;

[TestFixture]
public class WebViewDocumentNamingTests
{
    [Test]
    public void GetResourceName_DerivesTheNameFromThePageHost()
    {
        var resourceName = WebViewDocumentNaming.GetResourceName("https://scratch.mit.edu/projects/1");

        resourceName.Should().Be("scratch_mit_edu.webview");
    }

    [Test]
    public void GetResourceName_KeepsHyphensAndDropsTheLeadingSeparator()
    {
        // A hyphen is valid in a resource name, so it survives; the dot before the host's first label
        // becomes an underscore and is then trimmed from the front.
        var resourceName = WebViewDocumentNaming.GetResourceName("https://celbridge-org.example.com/");

        resourceName.Should().Be("celbridge-org_example_com.webview");
    }

    [Test]
    public void GetResourceName_FallsBackWhenTheUrlHasNoHost()
    {
        // A URL that is not absolute, or whose host sanitises away to nothing, still has to yield a
        // usable name for the dialog to open on.
        var resourceName = WebViewDocumentNaming.GetResourceName("not a url");

        resourceName.Should().Be("page.webview");
    }
}
