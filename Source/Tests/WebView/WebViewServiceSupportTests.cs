using Celbridge.Packages;
using Celbridge.Settings;
using Celbridge.WebHost.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.WebView;

[TestFixture]
public class WebViewServiceSupportTests
{
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IWorkspaceService _workspaceService = null!;
    private IDocumentsService _documentsService = null!;
    private IPackageService _packageService = null!;
    private WebViewService _webViewService = null!;

    [SetUp]
    public void SetUp()
    {
        _documentsService = Substitute.For<IDocumentsService>();
        _packageService = Substitute.For<IPackageService>();

        _workspaceService = Substitute.For<IWorkspaceService>();
        _workspaceService.DocumentsService.Returns(_documentsService);
        _workspaceService.PackageService.Returns(_packageService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(_workspaceService);
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(true);

        _documentsService.GetOpenDocuments().Returns(Array.Empty<OpenDocumentInfo>());
        _packageService.GetContributingPackage(Arg.Any<DocumentEditorId>()).Returns((Package?)null);

        _webViewService = new WebViewService(Substitute.For<IFeatureFlags>(), _workspaceWrapper);
    }

    [Test]
    public void GetWebViewToolSupport_DocumentNotOpen_AdvisesToOpenWithDocumentOpen()
    {
        var support = _webViewService.GetWebViewToolSupport(new ResourceKey("docs/missing.html"));

        support.IsSupported.Should().BeFalse();
        support.Reason.Should().Contain("docs/missing.html");
        support.Reason.Should().Contain("not open in the editor");
        support.Reason.Should().Contain("document_open");
    }

    [Test]
    public void GetWebViewToolSupport_ContributionEditorWithDevToolsBlocked_NamesThePackageAndPolicy()
    {
        var resource = new ResourceKey("budget.spreadsheet");
        var editorId = "celbridge.spreadsheet.spreadsheet-document";
        SetOpenDocuments(new OpenDocumentInfo(resource, new DocumentAddress(0, 0, 0), new DocumentEditorId(editorId)));

        var blockedPackage = new Package
        {
            Info = new PackageInfo
            {
                Id = "celbridge.spreadsheet",
                DevToolsBlocked = true
            }
        };
        _packageService.GetContributingPackage(new DocumentEditorId(editorId)).Returns(blockedPackage);

        var support = _webViewService.GetWebViewToolSupport(resource);

        support.IsSupported.Should().BeFalse();
        support.Reason.Should().Contain("celbridge.spreadsheet");
        support.Reason.Should().Contain("DevToolsBlocked");
        support.Reason.Should().Contain("policy");
    }

    [Test]
    public void GetWebViewToolSupport_NoWorkspaceLoaded_ReportsNoProjectLoaded()
    {
        // Without a workspace there can be no open documents, so the resource
        // cannot be supported. The reason names the missing project so callers
        // surface a useful message rather than a generic "not registered" one.
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        var support = _webViewService.GetWebViewToolSupport(new ResourceKey("any.html"));

        support.IsSupported.Should().BeFalse();
        support.Reason.Should().Contain("No project is loaded");
    }

    [Test]
    public void GetWebViewToolSupport_OpenInOtherwiseSupportedEditor_ReportsSupported()
    {
        var resource = new ResourceKey("notes/note.note");
        var editorId = "celbridge.notes.note-document";
        SetOpenDocuments(new OpenDocumentInfo(resource, new DocumentAddress(0, 0, 0), new DocumentEditorId(editorId)));

        var allowedPackage = new Package
        {
            Info = new PackageInfo
            {
                Id = "celbridge.notes",
                DevToolsBlocked = false
            }
        };
        _packageService.GetContributingPackage(new DocumentEditorId(editorId)).Returns(allowedPackage);

        var support = _webViewService.GetWebViewToolSupport(resource);

        support.IsSupported.Should().BeTrue();
        support.Reason.Should().BeNull();
    }

    private void SetOpenDocuments(params OpenDocumentInfo[] documents)
    {
        _documentsService.GetOpenDocuments().Returns(documents);
    }
}
