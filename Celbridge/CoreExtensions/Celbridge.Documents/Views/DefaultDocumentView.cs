using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

public sealed partial class DefaultDocumentView : DocumentView
{
    private IResourceRegistry _resourceRegistry;

    public DefaultDocumentViewModel ViewModel { get; }

    public DefaultDocumentView(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        ViewModel = serviceProvider.GetRequiredService<DefaultDocumentViewModel>();

        _resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        var textBox = new TextBox()
            .Text(x => x.Bind(() => ViewModel.Text)
                        .Mode(BindingMode.TwoWay)
                        .UpdateSourceTrigger(UpdateSourceTrigger.PropertyChanged))
            .AcceptsReturn(true)
            .IsSpellCheckEnabled(false);

        //
        // Set the data context and control content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(textBox));
    }

    public override Result SetFileResource(ResourceKey fileResource)
    {
        var filePath = _resourceRegistry.GetResourcePath(fileResource);

        if (!File.Exists(filePath))
        {
            return Result.Fail($"File resource does not exist: {fileResource}");            
        }

        ViewModel.FileResource = fileResource;
        ViewModel.FilePath = filePath;

        return Result.Ok();
    }

    public override async Task<Result> LoadContent()
    {
        return await ViewModel.LoadDocument();
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
    {
        return await ViewModel.SaveDocument();
    }
}
