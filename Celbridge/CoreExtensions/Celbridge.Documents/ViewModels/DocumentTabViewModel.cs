using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Explorer;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

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

    public IDocumentView? DocumentView { get; set; }

    public DocumentTabViewModel(
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _commandService = commandService;
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
    }

    private void OnResourceRegistryUpdatedMessage(object recipient, ResourceRegistryUpdatedMessage message)
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

    private void OnResourceKeyChangedMessage(object recipient, ResourceKeyChangedMessage message)
    {
        Guard.IsNotNull(DocumentView);

        if (message.SourceResource == FileResource)
        {
            FileResource = message.DestResource;
            DocumentName = message.DestResource.ResourceName;
            FilePath = message.DestPath;

            DocumentView.SetFileResourceAndPath(FileResource, FilePath);
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
            UnregisterMessageHandlers();
            return Result<bool>.Ok(true);
        }

        var canClose = forceClose || await DocumentView.CanCloseDocument();
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
                var failure = Result<bool>.Fail($"Saving document failed for file resource: '{FileResource}'");
                failure.MergeErrors(saveResult);
                return failure;
            }
        }

        UnregisterMessageHandlers();

        // Notify the DocumentView that the document is about to close
        DocumentView.OnDocumentClosing();

        return Result<bool>.Ok(true);
    }

    private void UnregisterMessageHandlers()
    {
        _messengerService.Unregister<ResourceRegistryUpdatedMessage>(this);
        _messengerService.Unregister<ResourceKeyChangedMessage>(this);
    }
}
