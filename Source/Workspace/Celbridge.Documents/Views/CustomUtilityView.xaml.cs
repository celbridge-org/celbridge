using Celbridge.Commands;
using Celbridge.Documents.ViewModels;
using Celbridge.Messaging;
using Celbridge.Packages;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Documents.Views;

/// <summary>
/// Hosts a custom utility in the Utility Panel, adapting the shared CustomEditorController to
/// panel chrome.
/// </summary>
public sealed partial class CustomUtilityView : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;
    private readonly CustomDocumentViewModel _viewModel;
    private readonly CustomEditorController _controller;
    private readonly CustomEditorFocusContext _panelFocusContext;

    // The utility's id, set on Bind. Used by the dock orchestration to address this panel.
    private EditorId _utilityId = EditorId.Empty;

    // The bound resolved editor, held so a lazy utility can initialize its WebView on first show.
    private ResolvedEditor? _resolvedEditor;

    public CustomUtilityView(IServiceProvider serviceProvider)
    {
        _messengerService = serviceProvider.GetRequiredService<IMessengerService>();
        _workspaceWrapper = serviceProvider.GetRequiredService<IWorkspaceWrapper>();
        _commandService = serviceProvider.GetRequiredService<ICommandService>();
        _viewModel = serviceProvider.GetRequiredService<CustomDocumentViewModel>();

        this.InitializeComponent();

        var openAsDocumentTooltip = serviceProvider.GetRequiredService<IStringLocalizer>().GetString("Utility_OpenAsDocument_Tooltip");
        ToolTipService.SetToolTip(OpenAsDocumentButton, openAsDocumentTooltip.ToString());
        AutomationProperties.SetName(OpenAsDocumentButton, openAsDocumentTooltip.ToString());

        // A utility in the panel is not a document, so it does not mark itself the active document on focus.
        // The registry still reports its Utility focus identity from the registration.
        _panelFocusContext = new CustomEditorFocusContext(
            WorkspacePanel.CustomUtility,
            () => { });

        _controller = new CustomEditorController(
            serviceProvider,
            _viewModel,
            CustomWebViewContainer,
            _panelFocusContext);
    }

    /// <summary>
    /// The utility's persistent editor controller. Owned by this panel for the workspace lifetime. The dock
    /// orchestration borrows it to present the utility in a document tab and hands it back when it returns.
    /// </summary>
    public CustomEditorController Controller => _controller;

    /// <summary>
    /// This panel's WebView container. Docking the utility back into the panel reparents the controller's
    /// WebView into it.
    /// </summary>
    public Panel PanelContainer => CustomWebViewContainer;

    /// <summary>
    /// The focus context the utility reports through while docked in the Utility Panel. Re-applied when the
    /// utility is docked back into the panel.
    /// </summary>
    public CustomEditorFocusContext PanelFocusContext => _panelFocusContext;

    public EditorId UtilityId => _utilityId;

    /// <summary>
    /// This utility's current dock location (the Utility Panel rail or a document tab).
    /// </summary>
    public DockLocation Location { get; set; } = DockLocation.UtilityPanel;

    private void OpenAsDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_utilityId.IsEmpty)
        {
            return;
        }

        _commandService.Execute<IDockUtilityCommand>(command =>
        {
            command.UtilityId = _utilityId;
            command.Location = DockLocation.Document;
        });
    }

    /// <summary>
    /// Binds the panel to its resolved editor and backing resource without creating the WebView.
    /// The backing file is expected to already exist, seeded before this call.
    /// </summary>
    public async Task<Result> BindAsync(ResolvedEditor resolvedEditor, ResourceKey resource, string displayName)
    {
        _resolvedEditor = resolvedEditor;
        _utilityId = resolvedEditor.EditorId;

        PanelHeaderControl.Title = displayName;

        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        var resolveResult = registry.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for utility resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var filePath = resolveResult.Value;

        _viewModel.FileResource = resource;
        _viewModel.FilePath = filePath;

        var operations = _workspaceWrapper.WorkspaceService.ResourceService.Operations;
        var writableState = await operations.GetWritableStateAsync(resource);
        _controller.SetWritableState(writableState);

        return Result.Ok();
    }

    /// <summary>
    /// Initializes the WebView for the bound resolved editor. The controller runs the initialization
    /// once; later calls await the same result, so this is safe to call on every show.
    /// </summary>
    public async Task<Result> EnsureInitializedAsync()
    {
        if (_resolvedEditor is null)
        {
            return Result.Fail("Cannot initialize utility: the view is not bound to a resolved editor");
        }

        var initResult = await _controller.InitializeAsync(_resolvedEditor);
        if (initResult.IsFailure)
        {
            return Result.Fail($"Failed to initialize utility: '{_viewModel.FileResource}'")
                .WithErrors(initResult);
        }

        return Result.Ok();
    }

    public bool HasUnsavedChanges => _viewModel.HasUnsavedChanges;

    public WritableState WritableState => _controller.WritableState;

    public ResourceKey FileResource => _viewModel.FileResource;

    public Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return _viewModel.UpdateSaveTimer(deltaTime);
    }

    /// <summary>
    /// Flushes the editor's content to the backing file and notifies save completion, mirroring the document
    /// save path so the view model's external-change tracking stays in sync.
    /// </summary>
    public async Task<Result> SaveAsync()
    {
        var result = await _controller.SaveContentAsync();
        if (result.IsSuccess)
        {
            _messengerService.Send(new DocumentSaveCompletedMessage(_viewModel.FileResource));
        }

        return result;
    }

    public void FocusPanel()
    {
        _controller.FocusWebView();
    }

    /// <summary>
    /// Tears down the WebView and host and unsubscribes the view model. Called on workspace unload.
    /// </summary>
    public void Teardown()
    {
        _controller.Teardown();
        _viewModel.Cleanup();
    }
}
