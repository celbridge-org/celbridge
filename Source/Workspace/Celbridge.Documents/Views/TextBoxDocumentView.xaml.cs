using Celbridge.Documents.ViewModels;

namespace Celbridge.Documents.Views;

public sealed partial class TextBoxDocumentView : DocumentView
{
    public DefaultDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

    public TextBoxDocumentView(
        IServiceProvider serviceProvider)
    {
        ViewModel = serviceProvider.GetRequiredService<DefaultDocumentViewModel>();

        this.InitializeComponent();
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

    protected override async Task<Result> SaveDocumentContentAsync()
    {
        return await ViewModel.SaveDocumentContent();
    }

    protected override void OnWritableStateChanged()
    {
        // WinUI TextBox.IsReadOnly keeps caret, selection, and copy working and
        // silently refuses typing.
        DocumentTextBox.IsReadOnly = WritableState != WritableState.Writable;
    }
}
