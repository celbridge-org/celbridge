using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

public abstract partial class DocumentView : UserControl, IDocumentView
{
    private IResourceRegistry? _resourceRegistry;

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
    /// Returns the ViewModel for this document view.
    /// Used by the base class to provide default SetFileResource and FileResource implementations.
    /// </summary>
    protected abstract DocumentViewModel DocumentViewModel { get; }

    public virtual ResourceKey FileResource => DocumentViewModel.FileResource;

    /// <summary>
    /// Sets the file resource for the document view.
    /// Validates the resource exists in the registry and on disk, then sets the ViewModel properties.
    /// Subclasses can override to add additional logic (call base first).
    /// </summary>
    public virtual Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var filePath = ResourceRegistry.GetResourcePath(fileResource);

        if (ResourceRegistry.GetResource(fileResource).IsFailure)
        {
            return Task.FromResult<Result>(Result.Fail($"File resource does not exist in resource registry: {fileResource}"));
        }

        if (!File.Exists(filePath))
        {
            return Task.FromResult<Result>(Result.Fail($"File resource does not exist on disk: {fileResource}"));
        }

        DocumentViewModel.FileResource = fileResource;
        DocumentViewModel.FilePath = filePath;

        return Task.FromResult(Result.Ok());
    }

    public abstract Task<Result> LoadContent();

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

    public virtual Task<Result> ApplyEditsAsync(IEnumerable<TextEdit> edits)
    {
        // Default implementation returns failure - only text editors support this
        return Task.FromResult<Result>(Result.Fail("This document type does not support text editing"));
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
}
