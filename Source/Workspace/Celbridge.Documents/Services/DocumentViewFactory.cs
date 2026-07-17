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
    /// The view is returned without content loaded. The caller drives
    /// SetFileResource and LoadContent.
    /// </summary>
    public async Task<Result<IDocumentView>> CreateAsync(
        ResourceKey fileResource,
        EditorInstanceId requestedEditorId)
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
            // Explicit editor request short-circuits the resolution chain. Failing
            // here rather than falling through surfaces wrong-editor requests
            // (e.g. an MCP call handing a .png to a code editor by mistake).
            return CreateForRequestedEditor(fileResource, requestedEditorId);
        }

        var sidecarView = await CreateFromSidecarPreferenceAsync(fileResource);
        if (sidecarView is not null)
        {
            return sidecarView.OkResult<IDocumentView>();
        }

        var extensionView = await CreateFromExtensionPreferenceAsync(fileResource);
        if (extensionView is not null)
        {
            return extensionView.OkResult<IDocumentView>();
        }

        var factoryView = CreateFromPriorityFactory(fileResource);
        if (factoryView is not null)
        {
            return factoryView.OkResult<IDocumentView>();
        }

        return CreateTextFallback(fileResource);
    }

    // Sidecar 'editor' field — the user's per-file "Open With X" choice. Wins
    // over per-extension preference and priority fallback. Returns the view
    // on success, or null when no preference is set, the editor is unregistered,
    // it cannot handle the resource, or construction fails (logged before
    // fall-through).
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

    // Per-extension preference: same fall-through contract as sidecar.
    private async Task<IDocumentView?> CreateFromExtensionPreferenceAsync(ResourceKey fileResource)
    {
        var extension = Path.GetExtension(fileResource.ToString()).ToLowerInvariant();
        var preferredEditorId = await _preferenceStore.GetExtensionPreferenceAsync(extension);
        if (preferredEditorId.IsEmpty)
        {
            return null;
        }

        var preferredFactoryResult = _documentEditorRegistry.GetFactoryById(preferredEditorId);
        if (preferredFactoryResult.IsFailure)
        {
            return null;
        }

        var preferredFactory = preferredFactoryResult.Value;
        if (!preferredFactory.CanHandleResource(fileResource))
        {
            return null;
        }

        var createResult = preferredFactory.CreateDocumentView(fileResource);
        if (createResult.IsSuccess)
        {
            return createResult.Value;
        }

        return null;
    }

    // Highest-priority factory for the resource. Placeholder factories
    // (package.toml, *.celbridge, *.document.toml) reserve extensions but
    // never produce a view, so they are skipped here.
    private IDocumentView? CreateFromPriorityFactory(ResourceKey fileResource)
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

        // Markdown is plain text, so when no Markdown editor is available it can still be edited as
        // text. This is the path taken on the Skia heads where the WebView-backed Markdown editor is
        // gated off. The plain TextBox is the built-in fallback. WebViewDocument and FileViewer are
        // not text-representable, so they correctly fail here rather than opening as text.
        if (viewType != DocumentViewType.TextDocument
            && viewType != DocumentViewType.Markdown)
        {
            return Result.Fail($"Failed to create document view for file: '{fileResource}'");
        }

        return CreateTextDocumentView(fileResource);
    }

    private Result<IDocumentView> CreateForRequestedEditor(ResourceKey fileResource, EditorInstanceId requestedEditorId)
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
        // Try every non-placeholder factory in priority order. Placeholders cannot
        // produce a view, so calling them here would burn cycles and fall through.
        foreach (var factory in _documentEditorRegistry.GetAllFactories().OrderBy(candidate => candidate.Priority))
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

        // Last-resort fallback. Used on non-Windows hosts (Monaco runs in
        // Windows-only WebView2) and when the code editor factory fails.
        // Stamped here because the TextBox is not produced by a factory.
        var textBoxView = _serviceProvider.GetRequiredService<TextBoxDocumentView>();
        textBoxView.EditorId = DocumentConstants.TextBoxFallbackEditorId;
        return textBoxView.OkResult<IDocumentView>();
    }

    private static bool IsCodeEditor(EditorInstanceId editorId)
    {
        return editorId == DocumentConstants.CodeEditorId;
    }
}
