using Celbridge.Documents.Views;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Unit tests for EditorPage, which defers what arrives for an editor page before it is listening and hands
/// it back when the page reports its load, and which tells a navigation that replaces the page from one
/// inside it.
/// </summary>
[TestFixture]
public class EditorPageTests
{
    private const string PageUri = "http://127.0.0.1:5000/package/code-editor/index.html?__hostToken=abc";
    private static readonly ResourceKey Document = new("site/page.html");
    private static readonly ResourceKey RenamedDocument = new("site/renamed.html");

    [Test]
    public void APageThatHasNotLoaded_DefersEverythingUntilItsLoad()
    {
        var page = new EditorPage();
        page.SetResource(Document);

        page.TryDeferLocation("{\"lineNumber\":3}").Should().BeTrue();
        page.TryDeferEditorState("{\"viewMode\":\"split\"}").Should().BeTrue();
        page.TryDeferRename().Should().BeTrue();
        page.TryDeferReload().Should().BeTrue();

        page.OnLoaded(RenamedDocument).Should().Be(new DeferredPageUpdates(
            Location: "{\"lineNumber\":3}",
            EditorStateJson: "{\"viewMode\":\"split\"}",
            Rename: true,
            Reload: true));
    }

    [Test]
    public void TheLatestLocationAndEditorState_AreTheOnesDeferred()
    {
        var page = new EditorPage();

        page.TryDeferLocation("first");
        page.TryDeferLocation("second");
        page.TryDeferEditorState("first");
        page.TryDeferEditorState("second");

        var deferred = page.OnLoaded(Document);

        deferred.Location.Should().Be("second");
        deferred.EditorStateJson.Should().Be("second");
    }

    [Test]
    public void ALoadedPage_TakesEverythingAtOnce()
    {
        var page = new EditorPage();
        page.OnLoaded(Document);

        page.TryDeferLocation("location").Should().BeFalse();
        page.TryDeferEditorState("state").Should().BeFalse();
        page.TryDeferRename().Should().BeFalse();
        page.TryDeferReload().Should().BeFalse();
    }

    [Test]
    public void ALoad_HandsBackWhatWasDeferredOnlyOnce()
    {
        var page = new EditorPage();
        page.OnNavigating(PageUri);
        page.TryDeferReload();
        page.TryDeferLocation("location");

        page.OnLoaded(Document);

        page.OnNavigating(PageUri);
        page.OnLoaded(Document).Should().Be(new DeferredPageUpdates(null, null, Rename: false, Reload: false));
    }

    [Test]
    public void ADeferredRename_IsHandedBackOnlyWhenThePageWasToldOfAnotherDocument()
    {
        // A page that loaded after the rename was already given the new name, so telling it again would
        // reload a preview that has just loaded.
        var page = new EditorPage();
        page.TryDeferRename();
        page.SetResource(RenamedDocument);

        page.OnLoaded(RenamedDocument).Rename.Should().BeFalse();
    }

    [Test]
    public void ANavigationBackToThePagesOwnAddress_MakesItWaitForItsNextLoad()
    {
        var page = new EditorPage();
        page.OnNavigating(PageUri);
        page.OnLoaded(Document);

        // A reload of the page carries a new query, and is still the page's own address.
        page.OnNavigating("http://127.0.0.1:5000/package/code-editor/index.html?__hostToken=def");

        page.IsLoaded.Should().BeFalse();
        page.TryDeferReload().Should().BeTrue();
    }

    [Test]
    public void ANavigationOfAFrameInsideThePage_LeavesItLoaded()
    {
        var page = new EditorPage();
        page.OnNavigating(PageUri);
        page.OnLoaded(Document);

        page.OnNavigating("http://127.0.0.1:5000/package/code-editor/markdown-preview/iframe.html");

        page.IsLoaded.Should().BeTrue();
        page.TryDeferReload().Should().BeFalse();
    }

    [Test]
    public void AReset_ForgetsThePageAndEverythingDeferredForIt()
    {
        var page = new EditorPage();
        page.OnNavigating(PageUri);
        page.SetResource(Document);
        page.TryDeferReload();
        page.TryDeferLocation("location");

        page.Reset();

        page.IsLoaded.Should().BeFalse();
        page.Resource.Should().Be(ResourceKey.Empty);
        page.OnLoaded(Document).Should().Be(new DeferredPageUpdates(null, null, Rename: false, Reload: false));

        // The next navigation is taken as the page's own again.
        page.OnNavigating("http://127.0.0.1:5000/package/other-editor/index.html");
        page.IsLoaded.Should().BeFalse();
    }
}
