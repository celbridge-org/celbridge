using Celbridge.Documents;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Helpers for tests that need a real SidecarPairingService — typically tests
/// that exercise code paths reading the sidecar report or per-file Sidecar
/// pairing through the resource registry.
/// </summary>
internal static class SidecarPairingTestHelper
{
    /// <summary>
    /// Builds a stub that returns an empty pairing result on every call. Use
    /// for tests that exercise the registry but do not care about sidecar
    /// classification (most ResourceRegistry tests). Avoids needing a real
    /// workspace wrapper.
    /// </summary>
    public static ISidecarPairingService BuildEmptyStub()
    {
        var stub = Substitute.For<ISidecarPairingService>();
        var emptyReport = new SidecarReport(
            Healthy: Array.Empty<ResourceKey>(),
            Broken: Array.Empty<ResourceKey>(),
            Orphan: Array.Empty<ResourceKey>());
        var emptyResult = new SidecarPairingResult(
            emptyReport,
            new Dictionary<ResourceKey, ResourceKey>());
        stub.ComputePairings(Arg.Any<IFolderResource>(), Arg.Any<IRootHandlerRegistry>())
            .Returns(emptyResult);
        return stub;
    }

    /// <summary>
    /// Builds a real SidecarPairingService wrapped around an editor registry
    /// that claims no factories. Every parentless .cel file is classified as
    /// an orphan, which matches the default expectation for tests that are
    /// not exercising standalone-form recognition.
    /// </summary>
    public static SidecarPairingService BuildPairingServiceWithNoFactories()
    {
        var editorRegistry = Substitute.For<IDocumentEditorRegistry>();
        editorRegistry.GetFactory(Arg.Any<ResourceKey>())
            .Returns(Result<IDocumentEditorFactory>.Fail("no factory"));
        return BuildPairingService(editorRegistry);
    }

    /// <summary>
    /// Builds a real SidecarPairingService wrapped around the supplied editor
    /// registry. Use when the test wants to stub specific standalone-form
    /// recognition rules (e.g. package.cel, foo.webview.cel).
    /// </summary>
    public static SidecarPairingService BuildPairingService(IDocumentEditorRegistry editorRegistry)
    {
        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.DocumentEditorRegistry.Returns(editorRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(documentsService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);
        workspaceWrapper.IsWorkspacePageLoaded.Returns(true);

        return new SidecarPairingService(
            Substitute.For<ILogger<SidecarPairingService>>(),
            workspaceWrapper);
    }
}
