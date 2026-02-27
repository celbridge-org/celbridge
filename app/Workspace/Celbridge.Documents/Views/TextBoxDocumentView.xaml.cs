using Celbridge.Documents.ViewModels;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

public sealed partial class TextBoxDocumentView : DocumentView
{
    private readonly IResourceRegistry _resourceRegistry;

    public DefaultDocumentViewModel ViewModel { get; }

    public TextBoxDocumentView(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        ViewModel = serviceProvider.GetRequiredService<DefaultDocumentViewModel>();

        _resourceRegistry = workspaceWrapper.WorkspaceService.ResourceService.Registry;

        this.InitializeComponent();
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
        return await ViewModel.LoadDocument();
    }

    public override bool HasUnsavedChanges => ViewModel.HasUnsavedChanges;

    public override Result<bool> UpdateSaveTimer(double deltaTime)
    {
        return ViewModel.UpdateSaveTimer(deltaTime);
    }

    public override async Task<Result> SaveDocument()
    {
        return await ViewModel.SaveDocument();
    }
}
