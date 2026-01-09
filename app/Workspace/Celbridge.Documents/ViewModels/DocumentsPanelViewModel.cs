using Celbridge.Commands;
using Celbridge.Messaging;
using Celbridge.Settings;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentsPanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IEditorSettings _editorSettings;
    private readonly IDocumentsService _documentsService;

    public bool IsExplorerPanelVisible => _editorSettings.IsContextPanelVisible;

    public bool IsInspectorPanelVisible => _editorSettings.IsInspectorPanelVisible;

    public DocumentsPanelViewModel(
        IMessengerService messengerService,
        ICommandService commandService,
        IEditorSettings editorSettings,
        IWorkspaceWrapper workspaceWrapper)
    {
        _messengerService = messengerService;
        _commandService = commandService;
        _editorSettings = editorSettings;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
    }

    public void OnViewLoaded()
    {
        var settings = _editorSettings as INotifyPropertyChanged;
        Guard.IsNotNull(settings);
        settings.PropertyChanged += EditorSettings_PropertyChanged;
    }

    public void OnViewUnloaded()
    {
        var settings = _editorSettings as INotifyPropertyChanged;
        Guard.IsNotNull(settings);
        settings.PropertyChanged -= EditorSettings_PropertyChanged;
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

    private void EditorSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 
        // Map the changed editor setting to the corresponding view model property.
        //
        if (e.PropertyName == nameof(IEditorSettings.IsContextPanelVisible))
        {
            OnPropertyChanged(nameof(IsExplorerPanelVisible));
        }
        else if (e.PropertyName == nameof(IEditorSettings.IsInspectorPanelVisible))
        {
            OnPropertyChanged(nameof(IsInspectorPanelVisible));
        }
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
}
