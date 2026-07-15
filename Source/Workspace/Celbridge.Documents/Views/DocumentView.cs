using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.WebHost;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

public abstract partial class DocumentView : UserControl, IDocumentView
{
    private IResourceRegistry? _resourceRegistry;
    private IResourceFileSystem? _resourceFileSystem;

    /// <summary>
    /// Provides access to the resource registry for file resource validation.
    /// Lazily initialized from the workspace wrapper.
    /// </summary>
    protected IResourceRegistry ResourceRegistry
    {
        get
        {
            if (_resourceRegistry is null)
            {
                var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
                _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;
            }
            return _resourceRegistry;
        }
    }

    /// <summary>
    /// Provides access to the resource file-system gateway.
    /// Lazily initialized from the workspace wrapper.
    /// </summary>
    protected IResourceFileSystem ResourceFileSystem
    {
        get
        {
            if (_resourceFileSystem is null)
            {
                var workspaceWrapper = ServiceLocator.AcquireService<IWorkspaceWrapper>();
                _resourceFileSystem = workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            }
            return _resourceFileSystem;
        }
    }

    /// <summary>
    /// Returns the ViewModel for this document view.
    /// Used by the base class to provide default SetFileResource and FileResource implementations.
    /// </summary>
    protected abstract DocumentViewModel DocumentViewModel { get; }

    public virtual ResourceKey FileResource => DocumentViewModel.FileResource;

    private DocumentEditorId _editorId = DocumentEditorId.Empty;

    // Set once by the constructing factory; throws on any subsequent set.
    public DocumentEditorId EditorId
    {
        get => _editorId;
        set
        {
            if (!_editorId.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"DocumentView.EditorId is set once and immutable thereafter. " +
                    $"Current value: '{_editorId}'; attempted to set: '{value}'.");
            }
            _editorId = value;
        }
    }

    /// <summary>
    /// Sets the file resource for the document view.
    /// Validates the resource exists in the registry and on disk, then sets the ViewModel properties.
    /// Subclasses can override to add additional logic (call base first).
    /// </summary>
    public virtual async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        // The registry only contains project: resources; virtual-root keys (utils:, temp:, logs:) are
        // addressable but never enumerated in the tree. The ResolveResourcePath + GetInfoAsync checks below
        // are root-resolver based and validate existence on all roots, so the registry-membership fast path
        // is gated to project resources.
        if (fileResource.Root == ResourceKey.DefaultRoot
            && ResourceRegistry.GetResource(fileResource).IsFailure)
        {
            return Result.Fail($"File resource does not exist in resource registry: {fileResource}");
        }

        var resolveResult = ResourceRegistry.ResolveResourcePath(fileResource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{fileResource}'")
                .WithErrors(resolveResult);
        }
        var filePath = resolveResult.Value;

        var infoResult = await ResourceFileSystem.GetInfoAsync(fileResource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind != StorageItemKind.File)
        {
            return Result.Fail($"File resource does not exist on disk: {fileResource}");
        }

        DocumentViewModel.FileResource = fileResource;
        DocumentViewModel.FilePath = filePath;

        return Result.Ok();
    }

    public abstract Task<Result> LoadContent();

    public WritableState WritableState { get; private set; } = WritableState.Writable;

    /// <summary>
    /// Applies a writable state to the document view. Stores the value and
    /// invokes OnWritableStateChanged so concrete views can apply the state
    /// to their native editor surface.
    /// </summary>
    public void SetWritableState(WritableState state)
    {
        if (WritableState == state)
        {
            return;
        }

        WritableState = state;
        OnWritableStateChanged();
    }

    /// <summary>
    /// Hook for concrete views to react to a writable-state change. Subclasses
    /// override to apply the state to their native editor surface.
    /// </summary>
    protected virtual void OnWritableStateChanged()
    {
    }

    public virtual bool HasUnsavedChanges => false;

    public virtual Result<bool> UpdateSaveTimer(double deltaTime)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Saves the document and sends a DocumentSaveCompletedMessage on success.
    /// Subclasses should override SaveDocumentContentAsync to implement document-specific save logic.
    /// </summary>
    public async Task<Result> SaveDocument()
    {
        var result = await SaveDocumentContentAsync();
        if (result.IsSuccess)
        {
            var messengerService = ServiceLocator.AcquireService<IMessengerService>();
            var message = new DocumentSaveCompletedMessage(FileResource);
            messengerService.Send(message);
        }
        return result;
    }

    /// <summary>
    /// Override this method to implement document-specific save logic.
    /// The base SaveDocument() will automatically send DocumentSaveCompletedMessage on success.
    /// </summary>
    protected virtual async Task<Result> SaveDocumentContentAsync()
    {
        await Task.CompletedTask;
        throw new NotImplementedException();
    }

    public virtual async Task<Result> NavigateToLocation(string location)
    {
        // Default implementation does nothing - subclasses can override for document-specific navigation
        await Task.CompletedTask;
        return Result.Ok();
    }

    public virtual async Task<bool> CanClose()
    {
        await Task.CompletedTask;
        return true;
    }

    public virtual async Task PrepareToClose()
    {
        await Task.CompletedTask;
    }

    public virtual Task<string?> TrySaveEditorStateAsync()
    {
        return Task.FromResult<string?>(null);
    }

    public virtual Task RestoreEditorStateAsync(string state)
    {
        return Task.CompletedTask;
    }

    // Registers a hosted web surface with the focus registry using the Documents-panel contract the web-view
    // document editors share: a focus gain reports the Documents panel and marks this the active document.
    // Pass the editor's edit target, or null for a surface that hosts none (an external-URL document).
    // releaseFocus drops the surface's caret when focus leaves it.
    protected void RegisterWebSurfaceFocus(WebView2 webView, IEditTarget? editTarget, Action releaseFocus)
    {
        var messengerService = ServiceLocator.AcquireService<IMessengerService>();
        var webViewFocusRegistry = ServiceLocator.AcquireService<IWebViewFocusRegistry>();

        var registration = new WebViewFocusRegistration(
            webView,
            WorkspacePanel.Documents,
            EditTarget: editTarget,
            ReleaseFocus: releaseFocus,
            OnFocusGained: () => messengerService.Send(new DocumentViewFocusedMessage(FileResource)));

        webViewFocusRegistry.Register(registration);
    }

    // Web-view-hosted editors override this to give their web content focus and report it to the focus
    // service. Views with no focusable surface (e.g. the plain text box) leave it as a no-op.
    public virtual void FocusDocument()
    {
    }
}
