using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Resources;
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
        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;
    }

    public virtual void OnViewLoaded()
    {
        _messengerService.Register<ResourceRegistryUpdatedMessage>(this, OnResourceRegistryUpdatedMessage);
        _messengerService.Register<ResourceKeyChangedMessage>(this, OnResourceKeyChangedMessage);
    }

    public virtual void OnViewUnloaded()
    {
        _messengerService.Unregister<ResourceRegistryUpdatedMessage>(this);
        _messengerService.Unregister<ResourceKeyChangedMessage>(this);
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

            DocumentView.UpdateDocumentResource(FileResource, FilePath);
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
            // The file no longer exists, so we presume that it was deleted intentionally.
            // Any pending save changes are discarded.
            return Result<bool>.Ok(true);
        }

        var canClose = forceClose || await DocumentView.CanCloseDocument();
        if (!canClose)
        {
            // The close operation was cancelled by the document view.
            return Result<bool>.Ok(false);
        }

        if (DocumentView.IsDirty)
        {
            var saveResult = await DocumentView.SaveDocument();
            if (saveResult.IsFailure)
            {
                var failure = Result<bool>.Fail($"Saving document failed for file resource: '{FileResource}'");
                failure.MergeErrors(saveResult);
                return failure;
            }
        }

        return Result<bool>.Ok(true);
    }
}
