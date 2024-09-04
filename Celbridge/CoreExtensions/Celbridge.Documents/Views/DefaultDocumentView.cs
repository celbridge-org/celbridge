using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;

namespace Celbridge.Documents.Views;

public sealed partial class DefaultDocumentView : DocumentView
{
    public DefaultDocumentViewModel ViewModel { get; }

    public DefaultDocumentView()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<DefaultDocumentViewModel>();

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

    public override void SetFileResourceAndPath(ResourceKey fileResource, string filePath)
    {
        ViewModel.FileResource = fileResource;
        ViewModel.FilePath = filePath;
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
