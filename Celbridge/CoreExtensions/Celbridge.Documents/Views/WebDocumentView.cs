using Celbridge.Documents.ViewModels;

namespace Celbridge.Documents.Views;

public sealed partial class WebDocumentView : UserControl, IDocumentView
{
    public WebDocumentViewModel ViewModel { get; }

    public WebDocumentView()
    {
        var serviceProvider = ServiceLocator.ServiceProvider;

        ViewModel = serviceProvider.GetRequiredService<WebDocumentViewModel>();

        var webView = new WebView2()
            .Source(x => x.Bind(() => ViewModel.Source));

        //
        // Set the data context and control content
        // 

        this.DataContext(ViewModel, (userControl, vm) => userControl
            .Content(webView));
    }

    public bool IsDirty => false;

    public Result<bool> UpdateSaveTimer(double deltaTime)
    {
        throw new NotImplementedException();
    }

    public async Task<Result> SaveDocument()
    {
        await Task.CompletedTask;
        throw new NotImplementedException();
    }
}
