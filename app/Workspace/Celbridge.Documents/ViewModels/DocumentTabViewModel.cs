using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Explorer;
using Celbridge.UserInterface;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentTabViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    [ObservableProperty]
    private IconDefinition _icon;

    [ObservableProperty]
    private ResourceKey _fileResource;

    partial void OnFileResourceChanged(ResourceKey oldValue, ResourceKey newValue)
    {
        _icon = _workspaceWrapper.WorkspaceService.ExplorerService.GetIconForResource(newValue);
    }

    [ObservableProperty]
    public string _documentName = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    public IDocumentView? DocumentView { get; set; }

    private ResourceKeyChangedMessage? _pendingResourceKeyChangedMessage;

    public DocumentTabViewModel(
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
        _resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

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

        // Use the default file icon until we can resolve the proper icon when the resource is populated.
        _icon = workspaceWrapper.WorkspaceService.ExplorerService.GetIconForResource(ResourceKey.Empty);
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
    /// Returns false if the document cancelled the close operation, e.g. via a confirmation dialog.
    /// The call fails if the close operation failed due to an error.
    /// </summary>
    public async Task<Result<bool>> CloseDocument(bool forceClose)
    {
        Guard.IsNotNull(DocumentView);

        if (!File.Exists(FilePath))
        {
            // The file no longer exists, so we assume that it was deleted intentionally.
            // Any pending save changes are discarded.

            // Clean up the DocumentView state before the document closes
            UnregisterMessageHandlers();
            DocumentView.PrepareToClose();

            return Result<bool>.Ok(true);
        }

        var canClose = forceClose || await DocumentView.CanClose();
        if (!canClose)
        {
            // The close operation was cancelled by the document view.
            return Result<bool>.Ok(false);
        }

        if (DocumentView.HasUnsavedChanges)
        {
            var saveResult = await DocumentView.SaveDocument();
            if (saveResult.IsFailure)
            {
                return Result<bool>.Fail($"Saving document failed for file resource: '{FileResource}'")
                    .WithErrors(saveResult);
            }
        }

        // Clean up the DocumentView state before the document closes
        UnregisterMessageHandlers();
        DocumentView.PrepareToClose();

        return Result<bool>.Ok(true);
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
