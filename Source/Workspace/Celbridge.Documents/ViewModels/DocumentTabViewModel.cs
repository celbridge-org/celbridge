using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;

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
    private readonly IResourceMonitor _resourceMonitor;
    private readonly IStringLocalizer _stringLocalizer;

    [ObservableProperty]
    private ResourceKey _fileResource;

    [ObservableProperty]
    public string _documentName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _editorDisplayName = string.Empty;

    [ObservableProperty]
    private bool _isSaveRetrying;

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
    /// True when this tab's file can be revealed in the Explorer. The Explorer shows the project tree, so
    /// a document opened from any other resource root has nothing to reveal.
    /// </summary>
    public bool CanRevealInExplorer => FileResource.Root == ResourceKey.DefaultRoot;

    /// <summary>
    /// Tooltip text for the tab. A utility tab shows its manifest description, falling back to its title
    /// when none is declared. An ordinary tab shows its file path plus the editor name when multiple
    /// editors are available. A tab whose file cannot be written says so below that.
    /// </summary>
    public string TabTooltip
    {
        get
        {
            var description = ComposeTabDescription();

            if (!IsSaveRetrying)
            {
                return description;
            }

            return _stringLocalizer.GetString("DocumentTab_Tooltip_SaveFailed", description);
        }
    }

    private string ComposeTabDescription()
    {
        if (IsUtilityEditor)
        {
            return string.IsNullOrEmpty(UtilityTooltip) ? DocumentName : UtilityTooltip;
        }

        if (string.IsNullOrEmpty(EditorDisplayName))
        {
            return FilePath;
        }

        return _stringLocalizer.GetString("DocumentTab_Tooltip_WithEditor", FilePath, EditorDisplayName);
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
        DocumentView?.SetAccessibleName(newValue);
    }

    partial void OnEditorDisplayNameChanged(string? oldValue, string newValue)
    {
        OnPropertyChanged(nameof(TabTooltip));
    }

    partial void OnFileResourceChanged(ResourceKey oldValue, ResourceKey newValue)
    {
        OnPropertyChanged(nameof(FileName));

        RefreshSaveState();

        SubscribeToFileChanges(newValue);
    }

    private IDocumentView? _documentView;

    /// <summary>
    /// The tab's document view. Setting it gives the view the tab's name as its accessible name.
    /// </summary>
    public IDocumentView? DocumentView
    {
        get => _documentView;
        set
        {
            _documentView = value;
            _documentView?.SetAccessibleName(DocumentName);
        }
    }

    private readonly IWorkspaceWrapper _workspaceWrapper;

    // The subscription to the file's changes, and the subscribed key. After a move, the tab subscribes to the new key
    // before the tab's FileResource takes it.
    private IResourceKeySubscription? _fileSubscription;
    private ResourceKey _subscribedResource;

    public DocumentTabViewModel(
        IMessengerService messengerService,
        ICommandService commandService,
        ILogger<DocumentTabViewModel> logger,
        IWorkspaceWrapper workspaceWrapper,
        IStringLocalizer stringLocalizer)
    {
        _messengerService = messengerService;
        _commandService = commandService;
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _stringLocalizer = stringLocalizer;
        _resourceMonitor = workspaceWrapper.WorkspaceService.ResourceService.Monitor;

        // Reordering a TabViewItem adds it in the new position before removing it from the old, so Unloaded
        // fires after Loaded. The message handlers are therefore registered here rather than on Loaded, and
        // unregistered in CloseDocument() when the tab actually closes. When the view unloads with the
        // workspace, WeakReferenceMessenger cleans the handlers up as this view model is collected.

        _messengerService.Register<ResourceKeyChangedMessage>(this, OnResourceKeyChangedMessage);
        _messengerService.Register<WorkspaceItemSaveRetriesChangedMessage>(this, OnSaveRetriesChanged);
    }

    private void OnSaveRetriesChanged(object recipient, WorkspaceItemSaveRetriesChangedMessage message)
    {
        IsSaveRetrying = message.RetryingResources.Contains(FileResource);
    }

    private void RefreshSaveState()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return;
        }

        // The retrying set is only sent when it changes, so a tab that appears later has missed it and
        // reads the current set instead.
        var retryingResources = _workspaceWrapper.WorkspaceService.GetRetryingResources();
        IsSaveRetrying = retryingResources.Contains(FileResource);
    }

    partial void OnIsSaveRetryingChanged(bool value)
    {
        OnPropertyChanged(nameof(TabTooltip));
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

    // Subscribes for the key before disposing the old subscription, so no change between the two is missed.
    private void SubscribeToFileChanges(ResourceKey resource)
    {
        if (_fileSubscription is not null
            && _subscribedResource == resource)
        {
            return;
        }

        var previousSubscription = _fileSubscription;
        _fileSubscription = null;
        _subscribedResource = resource;

        if (!resource.IsEmpty)
        {
            var handlers = new ResourceChangeHandlers(OnFileChanges, OnFileResync);
            var subscribeResult = _resourceMonitor.SubscribeToResources([resource], handlers);
            if (subscribeResult.IsSuccess)
            {
                _fileSubscription = subscribeResult.Value;
            }
            else
            {
                _logger.LogWarning(subscribeResult, $"Failed to subscribe to the changes to '{resource}'");
            }
        }

        previousSubscription?.Dispose();
    }

    // The check logs its own failures, so the task is not awaited.
    private void OnFileChanges(ResourceChangeBatch batch)
    {
        _ = CheckFileAsync();
    }

    // The lost events may have included a delete or an attribute change, so the file gets the same check as for a
    // change.
    private void OnFileResync(ResourceResync resync)
    {
        _ = CheckFileAsync();
    }

    // Checks the disk before closing the tab. Some editors and coding agents save by writing a temporary file, deleting
    // the original and renaming the temporary file. The file may then be back by the time the change arrives.
    private async Task CheckFileAsync()
    {
        var resource = _subscribedResource;

        try
        {
            // The workspace can unload while the disk is read. The check then stops once the workspace service is gone.
            if (!_workspaceWrapper.HasWorkspaceService)
            {
                return;
            }

            var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
            var infoResult = await resourceFileSystem.GetInfoAsync(resource);
            if (!_workspaceWrapper.HasWorkspaceService)
            {
                return;
            }

            if (infoResult.IsFailure
                || infoResult.Value.Kind != StorageItemKind.File)
            {
                // The file is gone, so force-close the document. The close goes through the command queue, so it
                // runs after any command already in progress.
                _commandService.Execute<ICloseDocumentCommand>(command =>
                {
                    command.FileResource = FileResource;
                    command.ForceClose = true;
                });
                return;
            }

            // Re-apply the writable state to the open document so external attribute
            // changes propagate into the editor's read-only signal without reopening the document.
            if (DocumentView is not null)
            {
                var operationService = _workspaceWrapper.WorkspaceService.ResourceService.Operations;
                var refreshedState = await operationService.GetWritableStateAsync(resource);
                if (DocumentView is not null
                    && refreshedState != DocumentView.WritableState)
                {
                    DocumentView.SetWritableState(refreshedState);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to check the file '{resource}'");
        }
    }

    // The tab subscribes to the new key at once, even when the file moved with a folder above it. The old key is
    // reported absent moments after the move. The document may still be loading under the new key then, and that
    // report must leave the tab open.
    private void OnResourceKeyChangedMessage(object recipient, ResourceKeyChangedMessage message)
    {
        var oldResource = FileResource;
        var sourceResource = message.SourceResource;
        var destResource = message.DestResource;
        if (!ResourceKeyHelper.TryMapKey(oldResource, sourceResource, destResource, out var newResource))
        {
            return;
        }

        SubscribeToFileChanges(newResource);

        var documentMessage = new DocumentResourceChangedMessage(oldResource, newResource);
        _messengerService.Send(documentMessage);
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
            // The file no longer exists, so there is nowhere left to save to and any pending changes
            // are discarded. An external folder rename arrives here as well as a deliberate delete,
            // with the document's edits still pending, so the discard is announced rather than silent.
            if (DocumentView.HasUnsavedChanges
                && !IsDockedUtility)
            {
                var discardedMessage = new WorkspaceItemSaveDiscardedMessage(FileResource);
                _messengerService.Send(discardedMessage);
            }

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

                // A docked utility keeps its view and its content when the tab closes, so nothing is
                // discarded.
                if (!IsDockedUtility)
                {
                    var discardedMessage = new WorkspaceItemSaveDiscardedMessage(FileResource);
                    _messengerService.Send(discardedMessage);
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

        _fileSubscription?.Dispose();
        _fileSubscription = null;
    }
}
