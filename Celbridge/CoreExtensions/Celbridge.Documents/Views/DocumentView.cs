using Celbridge.Resources;

namespace Celbridge.Documents.Views;

public abstract partial class DocumentView : UserControl, IDocumentView
{
    public virtual bool IsDirty => false;

    public virtual Result<bool> UpdateSaveTimer(double deltaTime)
    {
        throw new NotImplementedException();
    }

    public virtual async Task<bool> CanCloseDocument()
    {
        await Task.CompletedTask;
        return true;
    }

    public virtual async Task<Result> SaveDocument()
    {
        await Task.CompletedTask;
        throw new NotImplementedException();
    }

    public virtual void UpdateDocumentResource(ResourceKey fileResource, string filePath)
    {
        throw new NotImplementedException();
    }
}
