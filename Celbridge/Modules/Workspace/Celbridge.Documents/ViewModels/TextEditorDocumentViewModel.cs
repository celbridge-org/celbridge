using Celbridge.ExtensionAPI;
using Celbridge.Logging;
using Celbridge.ResourceData;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

using Path = System.IO.Path;

namespace Celbridge.Documents.ViewModels;

public partial class TextEditorDocumentViewModel : ObservableObject
{
    private const string ShowEditorKey = "ShowEditor";
    private const string ShowPreviewKey = "ShowPreview";

    private readonly ILogger<TextEditorDocumentViewModel> _logger;
    private readonly IDocumentsService _documentsService;
    private readonly IResourceDataService _resourceDataService;

    private ResourceKey _fileResource;

    [ObservableProperty]
    private bool _showEditor = true;

    [ObservableProperty]
    private bool _showPreview = true;

    public TextEditorDocumentViewModel(
        ILogger<TextEditorDocumentViewModel> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _documentsService = workspaceWrapper.WorkspaceService.DocumentsService;
        _resourceDataService = workspaceWrapper.WorkspaceService.ResourceDataService;
    }

    public void SetFileResource(ResourceKey fileResource)
    {
        // Todo: Unregister when the document closes
        _resourceDataService.UnregisterNotifier(fileResource, this);
        _resourceDataService.RegisterNotifier(fileResource, this, FileResource_PropertyChanged);

        _fileResource = fileResource;

        UpdatePanelVisibility();
    }

    public Result<PreviewProvider> GetPreviewProvider()
    {
        var fileExtension = Path.GetExtension(_fileResource);
        if (string.IsNullOrEmpty(fileExtension))
        {
            return Result<PreviewProvider>.Fail();
        }

        var getResult = _documentsService.GetPreviewProvider(fileExtension);
        if (getResult.IsFailure)
        {
            return getResult;
        }

        var provider = getResult.Value;

        return Result<PreviewProvider>.Ok(provider);
    }

    private void FileResource_PropertyChanged(ResourceKey resource, string propertyName)
    {
        if (propertyName == ShowEditorKey ||
            propertyName == ShowPreviewKey)
        {
            UpdatePanelVisibility();
        }
    }

    private void UpdatePanelVisibility()
    {
        try
        {
            ShowEditor = _resourceDataService.GetProperty(_fileResource, ShowEditorKey, true);
            ShowPreview = _resourceDataService.GetProperty(_fileResource, ShowPreviewKey, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.Message);
        }
    }
}
