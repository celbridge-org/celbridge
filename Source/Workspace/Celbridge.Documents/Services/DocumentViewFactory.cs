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
    /// The view is returned without content being loaded; the caller drives
    /// SetFileResource and LoadContent.
    /// </summary>
    public async Task<Result<IDocumentView>> CreateAsync(
        ResourceKey fileResource,
        DocumentEditorId requestedEditorId)
    {
        var pathFailure = CheckResourcePathResolves(fileResource);
        if (pathFailure is not null)
        {
            return pathFailure;
        }

        if (!requestedEditorId.IsEmpty)
        {
            // Explicit editor request short-circuits the resolution chain. Failing
            // here rather than falling through surfaces wrong-editor requests
            // (e.g. an MCP call handing a .png to a code editor by mistake).
            return CreateForRequestedEditor(fileResource, requestedEditorId);
        }

        var sidecarChoice = await TryCreateFromSidecarPreferenceAsync(fileResource);
        if (sidecarChoice is not null)
        {
            return sidecarChoice;
        }

        var extensionChoice = await TryCreateFromExtensionPreferenceAsync(fileResource);
        if (extensionChoice is not null)
        {
            return extensionChoice;
        }

        var factoryChoice = TryCreateFromPriorityFactory(fileResource);
        if (factoryChoice is not null)
        {
            return factoryChoice;
        }

        return CreateTextFallback(fileResource);
    }

    // Confirms the resource maps to a real backing path before any preference or
    // factory lookup runs. Returns null on success; the resolved path itself is
    // not used downstream.
    private Result<IDocumentView>? CheckResourcePathResolves(ResourceKey fileResource)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resolveResult = resourceRegistry.ResolveResourcePath(fileResource);
        if (resolveResult.IsFailure)
        {
            return Result<IDocumentView>.Fail($"Failed to resolve path for resource: '{fileResource}'")
                .WithErrors(resolveResult);
        }

        return null;
    }

    // The sidecar's 'editor' field records the user's last explicit "Open With X"
    // choice and wins over the per-extension preference and the priority fallback.
    // A stale or unregistered id returns null so the caller falls through.
    private async Task<Result<IDocumentView>?> TryCreateFromSidecarPreferenceAsync(ResourceKey fileResource)
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
            return createResult;
        }

        _logger.LogWarning(createResult,
            $"Sidecar editor '{sidecarEditorId}' failed to create view for '{fileResource}'; falling through");
        return null;
    }

    private async Task<Result<IDocumentView>?> TryCreateFromExtensionPreferenceAsync(ResourceKey fileResource)
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
            return createResult;
        }

        return null;
    }

    // Priority-based factory resolution. Placeholder factories (package.cel,
    // *.celbridge, *.document.cel) exist only for extension reservation and
    // never produce a view, so they are skipped here; the text fallback below
    // catches the open and routes it to the code editor without logging a
    // spurious "factory failed" warning.
    private Result<IDocumentView>? TryCreateFromPriorityFactory(ResourceKey fileResource)
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
            return createResult;
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

        if (viewType != DocumentViewType.TextDocument)
        {
            return Result.Fail($"Failed to create document view for file: '{fileResource}'");
        }

        return CreateTextDocumentView(fileResource);
    }

    private Result<IDocumentView> CreateForRequestedEditor(ResourceKey fileResource, DocumentEditorId requestedEditorId)
    {
        var getFactoryResult = _documentEditorRegistry.GetFactoryById(requestedEditorId);
        if (getFactoryResult.IsFailure)
        {
            return Result.Fail($"No document editor is registered with id '{requestedEditorId}'")
                .WithErrors(getFactoryResult);
        }
        var requestedFactory = getFactoryResult.Value;

        // The code editor is the universal "view as text" option offered through
        // "Open with...". The user can pick it for any text file, including ones
        // whose extension the code editor does not claim, so the extension match
        // is bypassed for this one editor id. Every other editor still goes
        // through CanHandleResource so wrong-editor requests fail loudly.
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

        // Last-resort fallback: the cross-platform plain TextBox. Kept for
        // non-Windows hosts (Monaco runs in WebView2, which is Windows-only)
        // and for the case where the code editor factory failed to construct.
        var textBoxView = _serviceProvider.GetRequiredService<TextBoxDocumentView>();
        return textBoxView.OkResult<IDocumentView>();
    }

    private static bool IsCodeEditor(DocumentEditorId editorId)
    {
        return editorId == DocumentConstants.CodeEditorId;
    }
}
