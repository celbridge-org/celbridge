using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

/// <summary>
/// Describes the result of a successful call to DocumentTabViewModel.CloseDocument.
/// Result.Fail is reserved for genuine framework errors. A failing save during close
/// is logged and the close proceeds, discarding the unsaved edits.
/// </summary>
public enum CloseDocumentOutcome
{
    /// <summary>
    /// The document was closed and its view was cleaned up.
    /// </summary>
    Closed,

    /// <summary>
    /// The close was cancelled.
    /// </summary>
    Cancelled,
}

public partial class DocumentTabViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly ILogger<DocumentTabViewModel> _logger;
    private readonly IResourceRegistry _resourceRegistry;

    [ObservableProperty]
    private ResourceKey _fileResource;

    [ObservableProperty]
    public string _documentName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _editorDisplayName = string.Empty;

    /// <summary>
    /// True when this tab borrows a utility's live view rather than holding a document of its own. Such a
    /// tab takes its title and icon from the manifest, offers no menu actions on the backing file, and
    /// announces neither an open nor a close because it was never opened as a document and is never
    /// really closed.
    /// </summary>
    [ObservableProperty]
    private bool _isDockedUtility;

    /// <summary>
    /// True when the tab's title comes from its editor rather than its file, so filename disambiguation
    /// leaves it alone. Set for a utility, and for an editor that names its own tabs.
    /// </summary>
    [ObservableProperty]
    private bool _hasFixedTitle;

    /// <summary>
    /// True when the tab's editor is a utility editor, so the tab presents that editor's identity: the
    /// manifest icon, title and tooltip in place of the ones its file would give it. A docked utility is
    /// always one of these, and so is any tab a utility editor opens.
    /// </summary>
    [ObservableProperty]
    private bool _isUtilityEditor;

    /// <summary>
    /// The Bootstrap glyph name for a utility tab's icon, sourced from the manifest. Empty for
    /// ordinary document tabs.
    /// </summary>
    [ObservableProperty]
    private string _utilityIconName = string.Empty;

    /// <summary>
    /// The manifest description shown as a utility tab's tooltip. Empty for ordinary document tabs and
    /// for utilities whose manifest declares no description.
    /// </summary>
    [ObservableProperty]
    private string _utilityTooltip = string.Empty;

    /// <summary>
    /// The editor that created this tab's document view.
    /// </summary>
    public EditorId EditorId { get; set; }

    /// <summary>
    /// Returns the file extension for the current resource.
    /// </summary>
    public string FileName => FileResource.ResourceName;

    /// <summary>
    /// Tooltip text for the tab. A utility tab shows its manifest description, falling back to its title
    /// when none is declared. An ordinary tab shows its file path plus the editor name when multiple
    /// editors are available.
    /// </summary>
    public string TabTooltip
    {
        get
        {
            if (IsUtilityEditor)
            {
                return string.IsNullOrEmpty(UtilityTooltip) ? DocumentName : UtilityTooltip;
            }

            if (string.IsNullOrEmpty(EditorDisplayName))
            {
                return FilePath;
            }

            return $"{FilePath} - {EditorDisplayName}";
        }
    }

    partial void OnFilePathChanged(string? oldValue, string newValue)
    {
        OnPropertyChanged(nameof(TabTooltip));
    }

    partial void OnUtilityTooltipChanged(string value)
    {
        OnPropertyChanged(nameof(TabTooltip));
    }

    partial void OnIsUtilityEditorChanged(bool value)
    {
        OnPropertyChanged(nameof(TabTooltip));
    }

    partial void OnDocumentNameChanged(string? oldValue, string newValue)
    {
        OnPropertyChanged(nameof(TabTooltip));
    }

    partial void OnEditorDisplayNameChanged(string? oldValue, string newValue)
    {
        OnPropertyChanged(nameof(TabTooltip));
    }

    partial void OnFileResourceChanged(ResourceKey oldValue, ResourceKey newValue)
    {
        OnPropertyChanged(nameof(FileName));
    }

    public IDocumentView? DocumentView { get; set; }

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private ResourceKeyChangedMessage? _pendingResourceKeyChangedMessage;

    public DocumentTabViewModel(
        IMessengerService messengerService,
        ICommandService commandService,
        ILogger<DocumentTabViewModel> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _commandService = commandService;
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        // Reordering a TabViewItem adds it in the new position before removing it from the old, so Unloaded
        // fires after Loaded. The message handlers are therefore registered here rather than on Loaded, and
        // unregistered in CloseDocument() when the tab actually closes. When the view unloads with the
        // workspace, WeakReferenceMessenger cleans the handlers up as this view model is collected.

        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdatedMessage);
        _messengerService.Register<ResourceKeyChangedMessage>(this, OnResourceKeyChangedMessage);
    }

    /// <summary>
    /// Returns true if more than one editor is registered for this document's file extension,
    /// meaning a "Reopen with..." menu option is worth showing to the user. Returns false during
    /// workspace teardown.
    /// </summary>
    public bool HasMultipleCompatibleEditors()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return false;
        }

        var factories = _workspaceWrapper.WorkspaceService.DocumentsService.DocumentEditorRegistry
            .GetUserPickableFactoriesForResource(FileResource);

        return factories.Count >= 2;
    }

    private async void OnResourceRegistryUpdatedMessage(object recipient, ResourceRegistryUpdatedMessage message)
    {
        if (_pendingResourceKeyChangedMessage is not null)
        {
            // This open document's resource has been renamed just prior to this registry update.
            // Tell the document service to update the file resource for the document.
            // The writable-state refresh below is skipped on this path: the re-bind for the new
            // key applies the writable state through the open flow.

            var oldResource = _pendingResourceKeyChangedMessage.SourceResource;
            var newResource = _pendingResourceKeyChangedMessage.DestResource;
            _pendingResourceKeyChangedMessage = null;

            var documentMessage = new DocumentResourceChangedMessage(oldResource, newResource);
            _messengerService.Send(documentMessage);
            return;
        }

        // Check if the open document is in the updated resource registry
        var getResult = _resourceRegistry.GetResource(FileResource);
        if (getResult.IsFailure)
        {
            // The file may have been temporarily deleted as part of a "write temp, delete original,
            // rename temp" save pattern used by some editors and coding agents. Check if the file
            // still exists on disk before closing. The resource registry may not have caught up
            // with the rename yet.
            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            var infoResult = await resourceFileSystem.GetInfoAsync(FileResource);
            if (infoResult.IsSuccess
                && infoResult.Value.Kind == StorageItemKind.File)
            {
                return;
            }

            // The resource no longer exists, so force-close the document. Routed through a command
            // instead of calling CloseDocument() directly to help avoid race conditions.
            _commandService.Execute<ICloseDocumentCommand>(command =>
            {
                command.FileResource = FileResource;
                command.ForceClose = true;
            });
            return;
        }

        // Re-apply the writable state to the open document so external attribute
        // changes (or any other source-of-truth refresh) propagate into the editor's
        // read-only signal without reopening the document.
        if (DocumentView is not null)
        {
            var operationService = _workspaceWrapper.WorkspaceService.ResourceService.Operations;
            var refreshedState = await operationService.GetWritableStateAsync(FileResource);
            if (refreshedState != DocumentView.WritableState)
            {
                DocumentView.SetWritableState(refreshedState);
            }
        }
    }

    private void OnResourceKeyChangedMessage(object recipient, ResourceKeyChangedMessage message)
    {
        if (message.SourceResource == FileResource)
        {
            // We should never receive multiple ResourceKeyChangedMessages for the same resource before the next registry update.
            Guard.IsNull(_pendingResourceKeyChangedMessage);

            // Delay handling the message until the next ResourceRegistryUpdatedMessage is received.
            _pendingResourceKeyChangedMessage = message;
        }
    }

    /// <summary>
    /// Close the opened document.
    /// forceClose forces the document to close without allowing the document to cancel the close operation.
    /// </summary>
    public async Task<Result<CloseDocumentOutcome>> CloseDocument(bool forceClose)
    {
        Guard.IsNotNull(DocumentView);

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var closeInfoResult = await resourceFileSystem.GetInfoAsync(FileResource);
        if (closeInfoResult.IsFailure
            || closeInfoResult.Value.Kind != StorageItemKind.File)
        {
            // The file no longer exists, so we assume that it was deleted intentionally.
            // Any pending save changes are discarded.

            // Clean up the DocumentView state before the document closes
            UnregisterMessageHandlers();
            await DocumentView.PrepareToClose();

            NotifyDocumentClosed();

            return Result<CloseDocumentOutcome>.Ok(CloseDocumentOutcome.Closed);
        }

        var canClose = forceClose || await DocumentView.CanClose();
        if (!canClose)
        {
            // The document view refused to close.
            return Result<CloseDocumentOutcome>.Ok(CloseDocumentOutcome.Cancelled);
        }

        if (DocumentView.HasUnsavedChanges)
        {
            var saveResult = await DocumentView.SaveAsync();
            if (saveResult.IsFailure)
            {
                // A non-editable document (locked file, read-only attribute) or any other
                // permanently-refused save would otherwise jam the close path forever.
                // Discard the unsaved edits and proceed to teardown.
                _logger.LogWarning(saveResult, $"Saving document failed during close. Discarding unsaved edits for file resource: '{FileResource}'");

                // If the cached writable state still reads Writable, an external attribute change
                // probably slipped past the watcher. Schedule a resource update so the cache catches
                // up. Debouncing inside the resource service coalesces bursts.
                if (DocumentView.WritableState == WritableState.Writable)
                {
                    _commandService.Execute<IUpdateResourcesCommand>();
                }
            }
        }

        // Clean up the DocumentView state before the document closes
        UnregisterMessageHandlers();
        await DocumentView.PrepareToClose();

        NotifyDocumentClosed();

        return Result<CloseDocumentOutcome>.Ok(CloseDocumentOutcome.Closed);
    }

    /// <summary>
    /// Announces that the document is open and its view is ready. Called by the panel once the tab is
    /// fully constructed, rather than fired from the DocumentView setter, because a resource rename
    /// replaces the view on a tab that was never closed.
    /// </summary>
    public void NotifyDocumentOpened()
    {
        if (IsDockedUtility)
        {
            return;
        }

        var documentOpenedMessage = new DocumentOpenedMessage(FileResource);
        _messengerService.Send(documentOpenedMessage);
    }

    // The closing half of the pair. It lives here rather than in the panel because every close path routes
    // through CloseDocument, including the reopen-with-a-different-editor path that removes the tab
    // directly. A docked utility never announced an open, so it announces no close either.
    private void NotifyDocumentClosed()
    {
        if (IsDockedUtility)
        {
            return;
        }

        var documentClosedMessage = new DocumentClosedMessage(FileResource);
        _messengerService.Send(documentClosedMessage);
    }

    public async Task<Result> ReloadDocument()
    {
        Guard.IsNotNull(DocumentView);

        return await DocumentView.LoadContent();
    }

    private void UnregisterMessageHandlers()
    {
        _messengerService.UnregisterAll(this);
    }
}
