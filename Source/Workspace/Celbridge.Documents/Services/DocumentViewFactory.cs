using Celbridge.Documents.Helpers;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Services;

/// <summary>
/// Picks the appropriate editor for a file resource and creates its document view.
/// </summary>
public class DocumentViewFactory
{
    private readonly IDocumentEditorRegistry _documentEditorRegistry;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly DocumentEditorPreferenceStore _preferenceStore;
    private readonly FileTypeClassifier _fileTypeClassifier;
    private readonly ILogger<DocumentViewFactory> _logger;

    public DocumentViewFactory(
        IDocumentEditorRegistry documentEditorRegistry,
        IWorkspaceWrapper workspaceWrapper,
        DocumentEditorPreferenceStore preferenceStore,
        FileTypeClassifier fileTypeClassifier,
        ILogger<DocumentViewFactory> logger)
    {
        _documentEditorRegistry = documentEditorRegistry;
        _workspaceWrapper = workspaceWrapper;
        _preferenceStore = preferenceStore;
        _fileTypeClassifier = fileTypeClassifier;
        _logger = logger;
    }

    /// <summary>
    /// Selects an editor for the given resource and constructs its document view.
    /// The view is returned without content loaded.
    /// </summary>
    public async Task<Result<IDocumentView>> CreateAsync(
        ResourceKey fileResource,
        EditorId requestedEditorId)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
        if (resolveResult.IsFailure)
        {
            return Result<IDocumentView>.Fail($"Failed to resolve path for resource: '{fileResource}'")
                .WithErrors(resolveResult);
        }

        if (!requestedEditorId.IsEmpty)
        {
            // An explicit editor request short-circuits the resolution chain, and fails rather
            // than falling through to another editor.
            return CreateForRequestedEditor(fileResource, requestedEditorId);
        }

        await foreach (var factory in ResolveFactoriesAsync(fileResource))
        {
            var createResult = factory.CreateDocumentView(fileResource);
            if (createResult.IsSuccess)
            {
                return createResult;
            }

            _logger.LogWarning(createResult,
                $"Editor '{factory.EditorId}' failed to create view for '{fileResource}'; falling through");
        }

        return Result<IDocumentView>.Fail($"No document editor can open the file: '{fileResource}'");
    }

    /// <summary>
    /// The editor the resource opens with when none is requested, or Empty when no editor can open it.
    /// </summary>
    public async Task<EditorId> ResolveEditorIdAsync(ResourceKey fileResource)
    {
        await foreach (var factory in ResolveFactoriesAsync(fileResource))
        {
            return factory.EditorId;
        }

        return EditorId.Empty;
    }

    // The factories that can open the resource, most preferred first: the sidecar's editor, the
    // project's editor association, the first factory in resolution order, and for a text file every
    // factory that claims it and then the code editor. Lazy, so a file is sniffed only when nothing
    // earlier claims it.
    private async IAsyncEnumerable<IDocumentEditorFactory> ResolveFactoriesAsync(ResourceKey fileResource)
    {
        var sidecarFactory = await GetSidecarFactoryAsync(fileResource);
        if (sidecarFactory is not null)
        {
            yield return sidecarFactory;
        }

        // The [celbridge].editor-associations entry whose extension is the longest matching suffix of
        // the file name. Entries are validated at workspace load, so a failed lookup just falls through.
        var associatedResult = _documentEditorRegistry.GetAssociatedEditorFactory(fileResource);
        if (associatedResult.IsSuccess)
        {
            yield return associatedResult.Value;
        }

        // First factory in resolution order: declared editors in declaration order, then built-ins in
        // their pinned order. Placeholder factories (package.toml, *.celbridge, *.editor.toml) reserve
        // extensions but never produce a view.
        var factoryResult = _documentEditorRegistry.GetFactory(fileResource);
        if (factoryResult.IsSuccess
            && !factoryResult.Value.IsPlaceholder)
        {
            yield return factoryResult.Value;
        }

        // Markdown is plain text, so it can still be edited as text when no Markdown editor is
        // available. WebViewDocument and FileViewer are not text-representable, so they never open as
        // text.
        var viewType = _fileTypeClassifier.GetDocumentViewType(fileResource);
        if (viewType != DocumentViewType.TextDocument
            && viewType != DocumentViewType.Markdown)
        {
            yield break;
        }

        foreach (var factory in _documentEditorRegistry.GetAllFactories())
        {
            if (!factory.IsPlaceholder
                && factory.CanHandleResource(fileResource))
            {
                yield return factory;
            }
        }

        // The bundled Monaco-based code editor, by id rather than by extension match, so it opens any
        // text file even when its extension is not in the code editor's extension list.
        var codeEditorResult = _documentEditorRegistry.GetFactoryById(DocumentConstants.CodeEditorId);
        if (codeEditorResult.IsSuccess)
        {
            yield return codeEditorResult.Value;
        }
    }

    // The sidecar 'editor' field is the user's per-file "Open With X" choice. Null when no override is
    // set, the editor is unregistered, or it cannot handle the resource.
    private async Task<IDocumentEditorFactory?> GetSidecarFactoryAsync(ResourceKey fileResource)
    {
        var sidecarEditorResult = await _preferenceStore.GetSidecarPreferenceAsync(fileResource);
        if (sidecarEditorResult.IsFailure
            || sidecarEditorResult.Value.IsEmpty)
        {
            return null;
        }

        var sidecarEditorId = sidecarEditorResult.Value;
        var sidecarFactoryResult = _documentEditorRegistry.GetFactoryById(sidecarEditorId);
        if (sidecarFactoryResult.IsFailure)
        {
            return null;
        }

        var sidecarFactory = sidecarFactoryResult.Value;
        if (!IsCodeEditor(sidecarEditorId)
            && !sidecarFactory.CanHandleResource(fileResource))
        {
            return null;
        }

        return sidecarFactory;
    }

    private Result<IDocumentView> CreateForRequestedEditor(ResourceKey fileResource, EditorId requestedEditorId)
    {
        var getFactoryResult = _documentEditorRegistry.GetFactoryById(requestedEditorId);
        if (getFactoryResult.IsFailure)
        {
            return Result.Fail($"No document editor is registered with id '{requestedEditorId}'")
                .WithErrors(getFactoryResult);
        }
        var requestedFactory = getFactoryResult.Value;

        // The code editor is the "view as text" option in Open With and may be
        // requested for any file, so the extension check is skipped for that one id.
        // Other editors still go through CanHandleResource.
        if (!IsCodeEditor(requestedEditorId)
            && !requestedFactory.CanHandleResource(fileResource))
        {
            return Result.Fail($"Document editor '{requestedEditorId}' cannot handle file resource: '{fileResource}'");
        }

        var createResult = requestedFactory.CreateDocumentView(fileResource);
        if (createResult.IsFailure)
        {
            return Result.Fail($"Document editor '{requestedEditorId}' failed to create view for: '{fileResource}'")
                .WithErrors(createResult);
        }

        return createResult;
    }

    private static bool IsCodeEditor(EditorId editorId)
    {
        return editorId == DocumentConstants.CodeEditorId;
    }
}
