using Celbridge.Commands;
using Celbridge.Documents.Services;
using Celbridge.Resources;
using Celbridge.Settings;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentsPanelViewModel : ObservableObject, IDocumentsManager
{
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IEditorSettings _editorSettings;

    internal IDocumentsView? DocumentsView { get; set; }

    public bool IsLeftPanelVisible => _editorSettings.IsLeftPanelVisible;

    public bool IsRightPanelVisible => _editorSettings.IsRightPanelVisible;

    public DocumentsPanelViewModel(
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper,
        IEditorSettings editorSettings)
    {
        _commandService = commandService;
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

    public void OnCloseDocumentRequested(ResourceKey fileResource)
    {
        _commandService.Execute<ICloseDocumentCommand>(command =>
        {
            command.FileResource = fileResource;
        });
    }

    public async Task<Result> OpenDocument(ResourceKey fileResource)
    {
        Guard.IsNotNull(DocumentsView);

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

        string filePath = resourceRegistry.GetResourcePath(fileResource);
        if (string.IsNullOrEmpty(filePath) ||
            !File.Exists(filePath))
        {
            return Result.Fail($"File resource path does not exist: '{filePath}'");
        }

        var openResult = await DocumentsView.OpenDocument(fileResource, filePath);
        if (openResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to open document for file resource: {fileResource}");
            failure.MergeErrors(openResult);
            return failure;
        }

        return Result.Ok();
    }

    public async Task<Result> CloseDocument(ResourceKey fileResource)
    {
        Guard.IsNotNull(DocumentsView);

        var closeResult = await DocumentsView.CloseDocument(fileResource);
        if (closeResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to close document for file resource: {fileResource}");
            failure.MergeErrors(closeResult);
            return failure;
        }

        return Result.Ok();
    }
}
