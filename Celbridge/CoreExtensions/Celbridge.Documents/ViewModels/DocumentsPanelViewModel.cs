using Celbridge.Commands;
using Celbridge.Documents.Views;
using Celbridge.Messaging;
using Celbridge.Explorer;
using Celbridge.Settings;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentsPanelViewModel : ObservableObject
{
    private readonly IMessengerService _messengerService;
    private readonly ICommandService _commandService;
    private readonly IDocumentsService _documentsService;
    private readonly IEditorSettings _editorSettings;

    private DocumentViewFactory _documentViewFactory = new();

    private bool _isWorkspaceLoaded;

    public bool IsLeftPanelVisible => _editorSettings.IsLeftPanelVisible;

    public bool IsRightPanelVisible => _editorSettings.IsRightPanelVisible;

    public DocumentsPanelViewModel(
        IMessengerService messengerService,
        ICommandService commandService,
        IDocumentsService documentsService,
        IEditorSettings editorSettings)
    {
        _messengerService = messengerService;
        _commandService = commandService;
        _documentsService = documentsService;
        _editorSettings = editorSettings;
    }

    public void OnViewLoaded()
    {
        var settings = _editorSettings as INotifyPropertyChanged;
        Guard.IsNotNull(settings);
        settings.PropertyChanged += EditorSettings_PropertyChanged;

        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoadedMessage);
    }

    public void OnViewUnloaded()
    {
        var settings = _editorSettings as INotifyPropertyChanged;
        Guard.IsNotNull(settings);
        settings.PropertyChanged -= EditorSettings_PropertyChanged;

        _messengerService.Unregister<WorkspaceLoadedMessage>(this);
    }

    private void OnWorkspaceLoadedMessage(object recipient, WorkspaceLoadedMessage message)
    {
        // This will remain true for the lifetime of this view model
        _isWorkspaceLoaded = true;
    }

    public async Task<Result<Control>> CreateDocumentView(ResourceKey fileResource, string filePath)
    {
        return await _documentViewFactory.CreateDocumentView(fileResource, filePath);
    }

    private void EditorSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 
        // Map the changed editor setting to the corresponding view model property.
        //
        if (e.PropertyName == nameof(IEditorSettings.IsLeftPanelVisible))
        {
            OnPropertyChanged(nameof(IsLeftPanelVisible));
        }
        else if (e.PropertyName == nameof(IEditorSettings.IsRightPanelVisible))
        {
            OnPropertyChanged(nameof(IsRightPanelVisible));
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
        // Notify the status bar about the current number of pending document saves.
        var message = new PendingDocumentSaveMessage(pendingSaveCount);
        _messengerService.Send(message);
    }

    public void OnOpenDocumentsChanged(List<ResourceKey> documentResources)
    {
        if (_isWorkspaceLoaded)
        {
            // Ignore change events that happen while loading the workspace and opening the
            // previously opened documents. 
            _documentsService.SetPreviousOpenDocuments(documentResources);
        }
    }

    public void OnSelectedDocumentChanged(ResourceKey documentResource)
    {
        if (_isWorkspaceLoaded)
        {
            // Ignore change events that happen while loading the workspace and opening the
            // previously opened documents. 
            _documentsService.SetPreviousSelectedDocument(documentResource);
        }

        // Notify the status panel that the selected document has changed
        var message = new SelectedDocumentChangedMessage(documentResource);
        _messengerService.Send(message);
    }
}
