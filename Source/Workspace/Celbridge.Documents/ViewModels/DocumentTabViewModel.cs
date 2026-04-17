using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

/// <summary>
/// Describes the result of a successful call to DocumentTabViewModel.CloseDocument.
/// A Result.Fail is still reserved for genuine errors (save failure, etc.).
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
    /// The editor that created this tab's document view.
    /// </summary>
    public DocumentEditorId EditorId { get; set; }

    /// <summary>
    /// Returns the file extension for the current resource, used by the FileIcon control.
    /// </summary>
    public string FileExtension => Path.GetExtension(FileResource.ResourceName);

    /// <summary>
    /// Tooltip text for the tab. Includes editor name when multiple editors are available.
    /// </summary>
    public string TabTooltip => string.IsNullOrEmpty(EditorDisplayName)
        ? FilePath
        : $"{FilePath} - {EditorDisplayName}";

    partial void OnFilePathChanged(string? oldValue, string newValue)
    {
        OnPropertyChanged(nameof(TabTooltip));
    }

    partial void OnEditorDisplayNameChanged(string? oldValue, string newValue)
    {
        OnPropertyChanged(nameof(TabTooltip));
    }

    partial void OnFileResourceChanged(ResourceKey oldValue, ResourceKey newValue)
    {
        OnPropertyChanged(nameof(FileExtension));
    }

    public IDocumentView? DocumentView { get; set; }

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private ResourceKeyChangedMessage? _pendingResourceKeyChangedMessage;

    public DocumentTabViewModel(
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        // We can't use the view's Loaded & Unloaded methods to register & unregister here.
        // Loaded and Unloaded are called when the UI element are added & removed from the visual tree.
        // When a TabViewItem is reordered, it is first added in the new position and then removed in the old position.
        // This means Unloaded is called first, followed by Load (opposite to what you might expect).

        // To work around this, we register the message handlers in the constructor and then unregister in the
        // CloseDocument() method if the tab is actually closed. There's one more case to consider, when the DocumentTabView
        // unloads (e.g. closing the open workspace). In this case, WeakReferenceMessenger should automatically clean up the
        // message handlers because the old DocumentTabViewModel has been destroyed.

        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdatedMessage);
        _messengerService.Register<ResourceKeyChangedMessage>(this, OnResourceKeyChangedMessage);
    }

    /// <summary>
    /// Returns true if more than one editor is registered for this document's file extension,
    /// meaning a "Reopen with..." menu option is worth showing to the user. Returns false during
    /// workspace teardown so the context menu gracefully hides the item when state is transient.
    /// </summary>
    public bool HasMultipleCompatibleEditors()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return false;
        }

        var extension = Path.GetExtension(FileResource.ToString()).ToLowerInvariant();
        var factories = _workspaceWrapper.WorkspaceService.DocumentsService.DocumentEditorRegistry
            .GetFactoriesForFileExtension(extension);

        return factories.Count >= 2;
    }

    private void OnResourceRegistryUpdatedMessage(object recipient, ResourceRegistryUpdatedMessage message)
    {
        if (_pendingResourceKeyChangedMessage is not null)
        {
            // This open document's resource has been renamed just prior to this registry update.
            // Tell the document service to update the file resource for the document.

            var oldResource = _pendingResourceKeyChangedMessage.SourceResource;
            var newResource = _pendingResourceKeyChangedMessage.DestResource;
            _pendingResourceKeyChangedMessage = null;

            var documentMessage = new DocumentResourceChangedMessage(oldResource, newResource);
            _messengerService.Send(documentMessage);
        }
        else
        {
            // Check if the open document is in the updated resource registry
            var getResult = _resourceRegistry.GetResource(FileResource);
            if (getResult.IsFailure)
            {
                // The file may have been temporarily deleted as part of a "write temp, delete original,
                // rename temp" save pattern used by some editors and coding agents. Check if the file
                // still exists on disk before closing. The resource registry may not have caught up
                // with the rename yet.
                if (File.Exists(FilePath))
                {
                    return;
                }

                // The resource no longer exists, so close the document.
                // We force the close operation because the resource no longer exists.
                // We use a command instead of calling CloseDocument() directly to help avoid race conditions.
                _commandService.Execute<ICloseDocumentCommand>(command =>
                {
                    command.FileResource = FileResource;
                    command.ForceClose = true;
                });
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

        if (!File.Exists(FilePath))
        {
            // The file no longer exists, so we assume that it was deleted intentionally.
            // Any pending save changes are discarded.

            // Clean up the DocumentView state before the document closes
            UnregisterMessageHandlers();
            await DocumentView.PrepareToClose();

            return Result<CloseDocumentOutcome>.Ok(CloseDocumentOutcome.Closed);
        }

        var canClose = forceClose || await DocumentView.CanClose();
        if (!canClose)
        {
            // The document view refused to close (user save-prompt dialog or programmatic veto).
            return Result<CloseDocumentOutcome>.Ok(CloseDocumentOutcome.Cancelled);
        }

        if (DocumentView.HasUnsavedChanges)
        {
            var saveResult = await DocumentView.SaveDocument();
            if (saveResult.IsFailure)
            {
                return Result<CloseDocumentOutcome>.Fail($"Saving document failed for file resource: '{FileResource}'")
                    .WithErrors(saveResult);
            }
        }

        // Clean up the DocumentView state before the document closes
        UnregisterMessageHandlers();
        await DocumentView.PrepareToClose();

        return Result<CloseDocumentOutcome>.Ok(CloseDocumentOutcome.Closed);
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
