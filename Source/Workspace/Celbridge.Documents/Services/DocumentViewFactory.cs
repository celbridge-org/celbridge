using Celbridge.Documents.Helpers;
using Celbridge.Documents.Views;
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
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentViewFactory> _logger;

    public DocumentViewFactory(
        IDocumentEditorRegistry documentEditorRegistry,
        IWorkspaceWrapper workspaceWrapper,
        DocumentEditorPreferenceStore preferenceStore,
        FileTypeClassifier fileTypeClassifier,
        IServiceProvider serviceProvider,
        ILogger<DocumentViewFactory> logger)
    {
        _documentEditorRegistry = documentEditorRegistry;
        _workspaceWrapper = workspaceWrapper;
        _preferenceStore = preferenceStore;
        _fileTypeClassifier = fileTypeClassifier;
        _serviceProvider = serviceProvider;
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

        var sidecarView = await CreateFromSidecarPreferenceAsync(fileResource);
        if (sidecarView is not null)
        {
            return sidecarView.OkResult<IDocumentView>();
        }

        var associatedEditorView = CreateFromEditorAssociations(fileResource);
        if (associatedEditorView is not null)
        {
            return associatedEditorView.OkResult<IDocumentView>();
        }

        var factoryView = CreateFromResolvedFactory(fileResource);
        if (factoryView is not null)
        {
            return factoryView.OkResult<IDocumentView>();
        }

        return CreateTextFallback(fileResource);
    }

    // Sidecar 'editor' field — the user's per-file "Open With X" choice. Wins over the
    // editor-associations map and the resolution-order fallback. Returns the view on success, or null
    // when no override is set, the editor is unregistered, it cannot handle the resource, or
    // construction fails (logged before fall-through).
    private async Task<IDocumentView?> CreateFromSidecarPreferenceAsync(ResourceKey fileResource)
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

        var createResult = sidecarFactory.CreateDocumentView(fileResource);
        if (createResult.IsSuccess)
        {
            return createResult.Value;
        }

        _logger.LogWarning(createResult,
            $"Sidecar editor '{sidecarEditorId}' failed to create view for '{fileResource}'; falling through");
        return null;
    }

    // Editor associations: the [celbridge].editor-associations entry whose extension is the
    // longest matching suffix of the file name. Entries are validated at workspace load, so a
    // failed lookup here just falls through.
    private IDocumentView? CreateFromEditorAssociations(ResourceKey fileResource)
    {
        var factoryResult = _documentEditorRegistry.GetAssociatedEditorFactory(fileResource);
        if (factoryResult.IsFailure)
        {
            return null;
        }

        var factory = factoryResult.Value;
        var createResult = factory.CreateDocumentView(fileResource);
        if (createResult.IsSuccess)
        {
            return createResult.Value;
        }

        _logger.LogWarning(createResult,
            $"Associated editor '{factory.EditorId}' failed to create view for '{fileResource}'; falling through");
        return null;
    }

    // First factory in resolution order for the resource: declared editors in declaration
    // order, then built-ins in host order. Placeholder factories (package.toml, *.celbridge,
    // *.editor.toml) reserve extensions but never produce a view, so they are skipped here.
    private IDocumentView? CreateFromResolvedFactory(ResourceKey fileResource)
    {
        var factoryResult = _documentEditorRegistry.GetFactory(fileResource);
        if (factoryResult.IsFailure
            || factoryResult.Value.IsPlaceholder)
        {
            return null;
        }

        var factory = factoryResult.Value;
        var createResult = factory.CreateDocumentView(fileResource);
        if (createResult.IsSuccess)
        {
            return createResult.Value;
        }

        _logger.LogWarning(createResult, $"Factory failed to create document view for: '{fileResource}'");
        return null;
    }

    private Result<IDocumentView> CreateTextFallback(ResourceKey fileResource)
    {
        var viewType = _fileTypeClassifier.GetDocumentViewType(fileResource);
        if (viewType == DocumentViewType.UnsupportedFormat)
        {
            return Result.Fail($"File resource is not a supported document format: '{fileResource}'");
        }

        // Markdown is plain text, so it can still be edited as text when no Markdown editor is
        // available. WebViewDocument and FileViewer are not text-representable, so they fail here
        // rather than opening as text.
        if (viewType != DocumentViewType.TextDocument
            && viewType != DocumentViewType.Markdown)
        {
            return Result.Fail($"Failed to create document view for file: '{fileResource}'");
        }

        return CreateTextDocumentView(fileResource);
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

    private Result<IDocumentView> CreateTextDocumentView(ResourceKey fileResource)
    {
        // Try every non-placeholder factory. Placeholders never produce a view.
        foreach (var factory in _documentEditorRegistry.GetAllFactories())
        {
            if (factory.IsPlaceholder)
            {
                continue;
            }

            if (factory.CanHandleResource(fileResource))
            {
                var createResult = factory.CreateDocumentView(fileResource);
                if (createResult.IsSuccess)
                {
                    return createResult;
                }
            }
        }

        // Default to the bundled Monaco-based code editor. Constructed by id, not
        // by extension match, so the code editor opens any text file even when
        // its extension is not in the code editor's extension list.
        var codeEditorFactoryResult = _documentEditorRegistry.GetFactoryById(DocumentConstants.CodeEditorId);
        if (codeEditorFactoryResult.IsSuccess)
        {
            var codeEditorResult = codeEditorFactoryResult.Value.CreateDocumentView(fileResource);
            if (codeEditorResult.IsSuccess)
            {
                return codeEditorResult;
            }

            _logger.LogWarning(codeEditorResult,
                $"Code editor '{DocumentConstants.CodeEditorId}' failed to create view for '{fileResource}'; using TextBoxDocumentView");
        }

        // Last-resort fallback, used when the code editor is unavailable or fails. The TextBox is
        // not produced by a factory, so its editor id is stamped here.
        var textBoxView = _serviceProvider.GetRequiredService<TextBoxDocumentView>();
        textBoxView.EditorId = DocumentConstants.TextBoxFallbackEditorId;
        return textBoxView.OkResult<IDocumentView>();
    }

    private static bool IsCodeEditor(EditorId editorId)
    {
        return editorId == DocumentConstants.CodeEditorId;
    }
}
