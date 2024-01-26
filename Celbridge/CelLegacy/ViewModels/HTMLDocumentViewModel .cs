using CommunityToolkit.Mvvm.Messaging;

namespace CelLegacy.ViewModels;

public partial class HTMLDocumentViewModel : ObservableObject
{
    private readonly IMessenger _messengerService;
    private readonly IProjectService _projectService;
    private readonly IDocumentService _documentService;
    private readonly IResourceService _resourceService;

    private string _path = string.Empty;

    public HTMLDocumentViewModel(IMessenger messengerService,
                                 IProjectService projectService,
                                 IDocumentService documentService,
                                 IResourceService resourceService)
    {
        _messengerService = messengerService;
        _projectService = projectService;
        _documentService = documentService;
        _resourceService = resourceService;

        _messengerService.Register<ResourcesChangedMessage>(this, OnResourcesChanged);
        _messengerService.Register<FileChangedMessage>(this, OnFileChanged);

        PropertyChanged += TextFileDocumentViewModel_PropertyChanged;
    }

    private void OnResourcesChanged(object recipient, ResourcesChangedMessage message)
    {
        foreach (var resourceId in message.Deleted)
        {
            if (Document.DocumentEntity.Id == resourceId)
            {
                CloseDocumentCommand.Execute(null);
            }
        }
    }

    private void OnFileChanged(object recipient, FileChangedMessage message)
    {
        if (message.Path == _path)
        {
            RefreshRequested?.Invoke();
        }
    }

    private IDocument? _document;
    public IDocument Document 
    {
        get => _document!;
        set
        {
            // Property can only be set once
            Guard.IsNull(_document);
            _document = value;
        }
    }

    public string Name => Document.DocumentEntity.Name;

    private string? _source;
    public string Source
    {
        get => _source!;
        set
        {
            SetProperty(ref _source, value);
        }
    }

    public IRelayCommand CloseDocumentCommand => new RelayCommand(OnCloseDocument_Executed);
    private void OnCloseDocument_Executed()
    {
        _messengerService.Unregister<ResourcesChangedMessage>(this);
        _messengerService.Unregister<FileChangedMessage>(this);

        // Close the document and remove it from the auto reload list
        Guard.IsNotNull(_document);
        _documentService.CloseDocument(_document.DocumentEntity, false);
    }

    public event Action? RefreshRequested;

    public async Task<Result> LoadDocumentAsync()
    {
        var fileResource = Document.DocumentEntity as FileResource;
        Guard.IsNotNull(fileResource);

        var project = _projectService.ActiveProject;
        Guard.IsNotNull(project);

        var pathResult = _resourceService.GetResourcePath(project, fileResource);
        if (pathResult is ErrorResult<string> error)
        {
            Log.Error(error.Message);
            return new ErrorResult(error.Message);
        }

        _path = pathResult.Data!;
        if (!File.Exists(_path))
        {
            return new ErrorResult($"Failed to load content. File '{_path}' does not exist.");
        }

        var htmlResource = Document.DocumentEntity as HTMLResource;
        Guard.IsNotNull(htmlResource);

        var url = htmlResource.StartURL;
        if (string.IsNullOrEmpty(url))
        {
            var fileUri = new Uri(Path.GetFullPath(_path));

            // Return the absolute URI string, which includes the file scheme
            url = fileUri.AbsoluteUri;
        }

        Source = url;

        // Dummy await command to avoid async warning
        await Task.CompletedTask;

        return new SuccessResult();
    }

    private void TextFileDocumentViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Source))
        {
            // Log.Information(Source);
        }
    }
}
