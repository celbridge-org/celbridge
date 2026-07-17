using Celbridge.Documents;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

/// <summary>
/// Document view for contribution-based editors, hosted via a WebView2. A thin adapter over
/// CustomEditorController: it satisfies the DocumentView contract (open, save timer, close, editor-state
/// persistence) and forwards the WebView-hosting work to the controller.
/// </summary>
public sealed partial class CustomDocumentView : DocumentView
{
    private readonly IMessengerService _messengerService;
    private readonly CustomDocumentViewModel _viewModel;
    private readonly CustomEditorController _controller;

    protected override DocumentViewModel DocumentViewModel => _viewModel;

    /// <summary>
    /// The editor contribution that configures this view.
    /// Must be set before LoadContent() is called.
    /// </summary>
    public EditorContribution? Contribution { get; set; }

    public CustomDocumentView(
        IServiceProvider serviceProvider,
        IMessengerService messengerService)
    {
        _messengerService = messengerService;
        _viewModel = serviceProvider.GetRequiredService<CustomDocumentViewModel>();

        this.InitializeComponent();

        var focusContext = new CustomEditorFocusContext(
            WorkspacePanel.Documents,
            () => _messengerService.Send(new DocumentViewFocusedMessage(_viewModel.FileResource)));

        _controller = new CustomEditorController(
            serviceProvider,
            _viewModel,
            CustomWebViewContainer,
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
            return Result.Fail("Cannot initialize custom view: Contribution is not set");
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
