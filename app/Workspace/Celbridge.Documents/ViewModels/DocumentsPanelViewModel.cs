using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentsPanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IDocumentsService _documentsService;

    public DocumentsPanelViewModel(
        IMessengerService messengerService,
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _commandService = commandService;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
    }

    public void OnViewUnloaded()
    {
        _messengerService.UnregisterAll(this);
    }

    public async Task<Result<IDocumentView>> CreateDocumentView(ResourceKey fileResource)
    {
        var createResult = await _documentsService.CreateDocumentView(fileResource);
        if (createResult.IsFailure)
        {
            return Result<IDocumentView>.Fail($"Failed to create document view for file resource: '{fileResource}'")
                .WithErrors(createResult);
        }
        var documentView = createResult.Value;

        return Result<IDocumentView>.Ok(documentView);
    }

    public void OnCloseDocumentRequested(ResourceKey fileResource)
    {
        _commandService.Execute<ICloseDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
        });
    }

    public void UpdatePendingSaveCount(int pendingSaveCount)
    {
        // Notify the StatusPanelViewModel about the current number of pending document saves.
        var message = new PendingDocumentSaveMessage(pendingSaveCount);
        _messengerService.Send(message);
    }

    public void OnOpenDocumentsChanged(List<ResourceKey> documentResources)
    {
        // Notify the DocumentsService about the current list of open documents.
        var message = new OpenDocumentsChangedMessage(documentResources);
        _messengerService.Send(message);
    }

    public void OnSelectedDocumentChanged(ResourceKey documentResource)
    {
        // Notify the DocumentsService about the currently selected documents.
        var message = new SelectedDocumentChangedMessage(documentResource);
        _messengerService.Send(message);
    }

    public void OnSectionRatiosChanged(List<double> ratios)
    {
        // Notify the DocumentsService about the section ratios change.
        var message = new SectionRatiosChangedMessage(ratios);
        _messengerService.Send(message);
    }
}
