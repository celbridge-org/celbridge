using Celbridge.Documents.ViewModels;

namespace Celbridge.Documents.Views;

public sealed partial class TextDocumentView : DocumentView
{
    public TextDocumentViewModel ViewModel { get; }

    public TextDocumentView()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<TextDocumentViewModel>();

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

    public override bool IsDirty => ViewModel.IsDirty;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<bool> CanCloseDocument()
    {
        await Task.CompletedTask;
        return true;
    }

    public override async Task<Result> SaveDocument()
    {
        return await ViewModel.SaveDocument();
    }
}
