namespace Celbridge.Documents.Views;

public abstract partial class DocumentView : UserControl, IDocumentView
{
    public abstract Task<Result> SetFileResource(ResourceKey fileResource);

    public abstract Task<Result> LoadContent();

    public virtual bool HasUnsavedChanges => false;

    public virtual Result<bool> UpdateSaveTimer(double deltaTime)
    {
        throw new NotImplementedException();
    }

    public virtual async Task<Result> SaveDocument()
    {
        await Task.CompletedTask;
        throw new NotImplementedException();
    }

    public virtual async Task<bool> CanClose()
    {
        await Task.CompletedTask;
        return true;
    }

    public virtual void PrepareToClose()
    {}
}
