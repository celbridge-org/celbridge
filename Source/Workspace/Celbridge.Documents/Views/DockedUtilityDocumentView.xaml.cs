using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

/// <summary>
/// Document view for a utility docked as a document: a utility whose presentation has moved from the Utility
/// Panel into a document tab. It borrows the utility's persistent CustomEditorController (owned by its
/// CustomUtilityView) rather than creating one, so the utility keeps a single WebView as it moves between
/// areas.
/// </summary>
public sealed partial class DockedUtilityDocumentView : DocumentView
{
    private readonly CustomEditorController _controller;
    private readonly CustomEditorFocusContext _focusContext;

    protected override DocumentViewModel DocumentViewModel => _controller.ViewModel;

    public DockedUtilityDocumentView(
        IMessengerService messengerService,
        CustomEditorController controller)
    {
        _controller = controller;

        this.InitializeComponent();

        // A docked utility reports the Documents panel and marks itself the active document on focus, matching
        // any other document tab. Docking back into the panel re-points the controller at its Utility context.
        _focusContext = new CustomEditorFocusContext(
            FocusPanelId.Documents,
            () => messengerService.Send(new DocumentViewFocusedMessage(FileResource)));
    }

    /// <summary>
    /// Moves the borrowed controller's WebView into this tab's container (the dock reparent). Synchronous so
    /// the reparent completes before the documents panel collapses the utility's panel view. The controller
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

    // The save tick flushes the panel view, not this tab, so the two never race.
    protected override async Task<Result> SaveDocumentContentAsync()
    {
        return await _controller.SaveContentAsync();
    }

    public override IEditTarget EditTarget => _controller;

    // The Utility Panel owns the controller and its view model, and keeps using both after this tab closes.
    protected override bool ClearsEditTargetOnClose => false;

    public override void FocusDocument()
    {
        _controller.FocusWebView();
    }
}
