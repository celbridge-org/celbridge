using Celbridge.Documents;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

/// <summary>
/// Document view for contribution-based editors, hosted via a WebView2. A thin adapter over
/// ContributionEditorController: it satisfies the DocumentView contract (open, save timer, close, editor-state
/// persistence) and forwards the WebView-hosting work to the controller.
/// </summary>
public sealed partial class ContributionDocumentView : DocumentView
{
    private readonly IMessengerService _messengerService;
    private readonly ContributionDocumentViewModel _viewModel;
    private readonly ContributionEditorController _controller;

    protected override DocumentViewModel DocumentViewModel => _viewModel;

    /// <summary>
    /// The document contribution that configures this view.
    /// Must be set before LoadContent() is called.
    /// </summary>
    public CustomDocumentEditorContribution? Contribution { get; set; }

    public ContributionDocumentView(
        IServiceProvider serviceProvider,
        IMessengerService messengerService)
    {
        _messengerService = messengerService;
        _viewModel = serviceProvider.GetRequiredService<ContributionDocumentViewModel>();

        this.InitializeComponent();

        var focusContext = new ContributionEditorFocusContext(
            WorkspacePanel.Documents,
            () => _messengerService.Send(new DocumentViewFocusedMessage(_viewModel.FileResource)));

        _controller = new ContributionEditorController(
            serviceProvider,
            _viewModel,
            ContributionWebViewContainer,
            focusContext);
    }

    public override bool HasUnsavedChanges => _viewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return _viewModel.UpdateSaveTimer(deltaTime);
    }

    protected override async Task<Result> SaveDocumentContentAsync()
    {
        return await _controller.SaveContentAsync();
    }

    protected override void OnWritableStateChanged()
    {
        _controller.SetWritableState(WritableState);
    }

    public override async Task<Result> LoadContent()
    {
        if (Contribution is null)
        {
            return Result.Fail("Cannot initialize contribution view: Contribution is not set");
        }

        return await _controller.InitializeAsync(Contribution);
    }

    public override async Task<Result> NavigateToLocation(string location)
    {
        return await _controller.NavigateToLocationAsync(location);
    }

    public override Task<string?> TrySaveEditorStateAsync()
    {
        return _controller.TrySaveEditorStateAsync();
    }

    public override Task RestoreEditorStateAsync(string state)
    {
        return _controller.RestoreEditorStateAsync(state);
    }

    public override async Task<bool> CanClose()
    {
        await Task.CompletedTask;

        // A utility declared closable = false in its manifest vetoes user-initiated close. The force-close
        // paths (resource deletion, project teardown) bypass CanClose, so the tab still tears down on unload.
        return Contribution?.UtilityDescriptor?.Closable ?? true;
    }

    public override async Task PrepareToClose()
    {
        _messengerService.UnregisterAll(this);

        _controller.Teardown();

        _viewModel.Cleanup();

        await base.PrepareToClose();
    }

    public override void FocusDocument()
    {
        _controller.FocusWebView();
    }
}
