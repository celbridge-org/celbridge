using Celbridge.Documents.ViewModels;
using Celbridge.Explorer;
using Celbridge.Workspace;

namespace Celbridge.Documents.Views;

public sealed partial class WebPageDocumentView : DocumentView
{
    private IResourceRegistry _resourceRegistry;

    public WebPageDocumentViewModel ViewModel { get; }

    private WebView2 _webView;

    public WebPageDocumentView(
        IServiceProvider serviceProvider,
        IWorkspaceWrapper workspaceWrapper)
    {
        ViewModel = serviceProvider.GetRequiredService<WebPageDocumentViewModel>();

        _resourceRegistry = workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        _webView = new WebView2()
            .Source(x => x.Bind(() => ViewModel.Source));

        // Fixes a visual bug where the WebView2 control would show a white background briefly when
        // switching between tabs. Similar issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        _webView.DefaultBackgroundColor = Colors.Transparent;

        //
        // Set the data context and control content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(_webView));
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
        return await ViewModel.LoadContent();
    }
}
