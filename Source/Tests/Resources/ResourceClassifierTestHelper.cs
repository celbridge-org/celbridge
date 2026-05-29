using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Helpers for tests that need a real ResourceClassifier — typically tests
/// that exercise code paths reading the .cel report or per-file Sidecar
/// pairing through the resource registry.
/// </summary>
internal static class ResourceClassifierTestHelper
{
    /// <summary>
    /// Builds a stub that returns an empty classification result on every call.
    /// Use for tests that exercise the registry but do not care about file
    /// classification (most ResourceRegistry tests). Avoids needing a real
    /// workspace wrapper.
    /// </summary>
    public static IResourceClassifier BuildEmptyStub()
    {
        var stub = Substitute.For<IResourceClassifier>();
        var emptyReport = new SidecarReport(
            Healthy: Array.Empty<ResourceKey>(),
            Broken: Array.Empty<ResourceKey>(),
            Orphan: Array.Empty<ResourceKey>());
        stub.ClassifyResources(Arg.Any<IFolderResource>(), Arg.Any<IRootHandlerRegistry>())
            .Returns(emptyReport);
        return stub;
    }

    /// <summary>
    /// Builds a real ResourceClassifier wrapped around an editor registry
    /// that claims no factories. Every parentless .cel file is classified as
    /// an orphan, which matches the default expectation for tests that are
    /// not exercising standalone-form recognition.
    /// </summary>
    public static ResourceClassifier BuildClassifierWithNoFactories()
    {
        // NSubstitute returns false for unconfigured bool methods, so the
        // standalone-form check naturally returns "no match" without any
        // explicit stubbing.
        var editorRegistry = Substitute.For<IDocumentEditorRegistry>();
        return BuildClassifier(editorRegistry);
    }

    /// <summary>
    /// Builds a real ResourceClassifier wrapped around the supplied editor
    /// registry. Use when the test wants to stub specific standalone-form
    /// recognition rules (e.g. foo.webview.cel, foo.note.cel).
    /// </summary>
    public static ResourceClassifier BuildClassifier(IDocumentEditorRegistry editorRegistry)
    {
        var documentsService = Substitute.For<IDocumentsService>();
        documentsService.DocumentEditorRegistry.Returns(editorRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.DocumentsService.Returns(documentsService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);
        workspaceWrapper.IsWorkspacePageLoaded.Returns(true);

        return new ResourceClassifier(
            Substitute.For<ILogger<ResourceClassifier>>(),
            workspaceWrapper);
    }
}
