using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

public sealed partial class TextBoxDocumentView : DocumentView
{
    private readonly TextBoxEditTarget _editTarget;
    private readonly ILogger<TextBoxDocumentView> _logger;

    public DefaultDocumentViewModel ViewModel { get; }

    protected override DocumentViewModel DocumentViewModel => ViewModel;

    public TextBoxDocumentView(
        IServiceProvider serviceProvider)
    {
        ViewModel = serviceProvider.GetRequiredService<DefaultDocumentViewModel>();
        _logger = serviceProvider.GetRequiredService<ILogger<TextBoxDocumentView>>();

        this.InitializeComponent();

        _editTarget = new TextBoxEditTarget(
            DocumentTextBox,
            serviceProvider.GetRequiredService<ICommandService>(),
            serviceProvider.GetRequiredService<ILogger<TextBoxEditTarget>>());
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

    public override IEditTarget EditTarget => _editTarget;

    public override void FocusDocument()
    {
        if (!DocumentTextBox.Focus(FocusState.Programmatic))
        {
            _logger.LogDebug("The text document did not take focus");
        }
    }
}
