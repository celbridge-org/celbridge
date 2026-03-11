using Celbridge.Messaging;

namespace Celbridge.Documents.Views;

public abstract partial class DocumentView : UserControl, IDocumentView
{
    public abstract ResourceKey FileResource { get; }

    public abstract Task<Result> SetFileResource(ResourceKey fileResource);

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
