using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

/// <summary>
/// Document view for a utility docked as a document: a utility whose presentation has moved from the Utility
/// Panel into a document tab. It borrows the utility's persistent CustomEditorController (owned by its
/// CustomUtilityView) rather than creating one, so the utility keeps a single WebView as it moves between
/// dock locations. The view is inert on saves (the owning panel drives the save tick) and never tears the
/// controller down on close; the documents panel reparents the WebView back to the panel instead.
/// </summary>
public sealed partial class DockedUtilityDocumentView : DocumentView
{
    private readonly CustomDocumentViewModel _viewModel;
    private readonly CustomEditorController _controller;
    private readonly CustomEditorFocusContext _focusContext;

    protected override DocumentViewModel DocumentViewModel => _viewModel;

    public DockedUtilityDocumentView(
        IServiceProvider serviceProvider,
        IMessengerService messengerService,
        CustomEditorController controller)
    {
        _controller = controller;
        _viewModel = serviceProvider.GetRequiredService<CustomDocumentViewModel>();

        this.InitializeComponent();

        // A docked utility reports the Documents panel and marks itself the active document on focus, matching
        // any other document tab. Docking back into the panel re-points the controller at its Utility context.
        _focusContext = new CustomEditorFocusContext(
            WorkspacePanel.Documents,
            () => messengerService.Send(new DocumentViewFocusedMessage(_viewModel.FileResource)));
    }

    /// <summary>
    /// Binds the view to the utility's already-existing resource so the tab and focus reporting resolve. The
    /// backing file was seeded by the owning panel at workspace load, so this skips the base existence check.
    /// </summary>
    public void Bind(ResourceKey fileResource, string filePath)
    {
        _viewModel.FileResource = fileResource;
        _viewModel.FilePath = filePath;
    }

    /// <summary>
    /// Moves the borrowed controller's WebView into this tab's container (the dock reparent). Synchronous so
    /// the reparent completes before the documents panel collapses the utility's panel surface. The controller
    /// is already live, so there is no init here.
    /// </summary>
    public void Dock()
    {
        _controller.Redock(DockedWebViewContainer, _focusContext);
    }

    public override async Task<Result> LoadContent()
    {
        await Task.CompletedTask;

        Dock();

        return Result.Ok();
    }

    public override void FocusDocument()
    {
        _controller.FocusWebView();
    }

    public override async Task PrepareToClose()
    {
        // The borrowed controller is never torn down here: docking back into the panel reparents its WebView to
        // the Utility Panel, which stays its owner. Only this view's own inert view model is released.
        _viewModel.Cleanup();

        await base.PrepareToClose();
    }
}
