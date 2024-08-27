using Celbridge.Documents.Services;
using Celbridge.Logging;
using Celbridge.Resources;
using Celbridge.Settings;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentsPanelViewModel : ObservableObject, IDocumentsManager
{
    private readonly ILogger<DocumentsPanelViewModel> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IEditorSettings _editorSettings;

    public bool IsLeftPanelVisible => _editorSettings.IsLeftPanelVisible;

    public bool IsRightPanelVisible => _editorSettings.IsRightPanelVisible;

    public DocumentsPanelViewModel(
        ILogger<DocumentsPanelViewModel> logger,
        IWorkspaceWrapper workspaceWrapper,
        IEditorSettings editorSettings)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;

        // Give the Documents Service a reference to this view model via the internal IDocumentsManager interface.
        var documentsService = _workspaceWrapper.WorkspaceService.DocumentsService as DocumentsService;
        Guard.IsNotNull(documentsService);
        documentsService.DocumentsManager = this;

        _editorSettings = editorSettings;

        var settings = _editorSettings as INotifyPropertyChanged;
        Guard.IsNotNull(settings);
        settings.PropertyChanged += EditorSettings_PropertyChanged;
    }

    public void OnViewLoaded()
    {}

    public void OnViewUnloaded()
    {
        var settings = _editorSettings as INotifyPropertyChanged;
        Guard.IsNotNull(settings);
        settings.PropertyChanged -= EditorSettings_PropertyChanged;
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

    public async Task<Result> OpenFileDocument(ResourceKey fileResource)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;

        var getResult = resourceRegistry.GetResource(fileResource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"File resource not found: '{fileResource}'");
        }

        var resource = getResult.Value as IFileResource;
        if (resource is null)
        {
            return Result.Fail($"Resource is not a file: '{fileResource}'");
        }

        // Check if the file is already open
        //  If so, activate the existing document
        //  If not, create a new document tab
        // Add resource to list of open documents
        // Persist list to workspace state

        // Todo: Add an interface to the DocumentsPanel view to allow the View Model to control it

        _logger.LogInformation($"Opening file resource document '{fileResource}'");

        await Task.CompletedTask;

        return Result.Ok();
    }
}
