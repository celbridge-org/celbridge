using Celbridge.Explorer;

namespace Celbridge.Documents.Views;

public abstract partial class DocumentView : UserControl, IDocumentView
{
    public abstract Task<Result> LoadContent();

    public virtual void SetFileResourceAndPath(ResourceKey fileResource, string filePath)
    {
        throw new NotImplementedException();
    }

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

    public virtual async Task<bool> CanCloseDocument()
    {
        await Task.CompletedTask;
        return true;
    }

    public virtual void OnDocumentClosing()
    {}
}
