using Celbridge.Documents.Views;
using Celbridge.FileViewer.ViewModels;
using Celbridge.Messaging;
using Celbridge.Workspace;

namespace Celbridge.FileViewer.Views;

public sealed partial class FileViewerDocumentView : WebView2DocumentView
{
    private readonly IResourceRegistry _resourceRegistry;

    public FileViewerDocumentViewModel ViewModel { get; }

    protected override ResourceKey FileResource => ViewModel.FileResource;

    public FileViewerDocumentView(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper,
        IMessengerService messengerService)
        : base(messengerService)
    {
        ViewModel = serviceProvider.GetRequiredService<FileViewerDocumentViewModel>();

        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        this.InitializeComponent();

        // Assign the WebView from XAML to the base class property
        WebView = FileWebView;
    }

    public override async Task<Result> SetFileResource(ResourceKey fileResource)
    {
        var filePath = _resourceRegistry.GetResourcePath(fileResource);

        if (_resourceRegistry.GetResource(fileResource).IsFailure)
        {
            return Result.Fail($"File resource does not exist in resource registry: {fileResource}");
        }

        if (!File.Exists(filePath))
        {
            return Result.Fail($"File resource does not exist on disk: {fileResource}");
        }

        ViewModel.FileResource = fileResource;
        ViewModel.FilePath = filePath;

        await Task.CompletedTask;

        return Result.Ok();
    }

    public override async Task<Result> LoadContent()
    {
        await InitializeWebViewAsync();

        return await ViewModel.LoadContent();
    }
}
