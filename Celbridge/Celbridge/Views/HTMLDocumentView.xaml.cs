using Celbridge.ViewModels;
using ColorCode.Compilation.Languages;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.Views
{
    public partial class HTMLDocumentView : TabViewItem, IDocumentView
    {
        public HTMLDocumentViewModel ViewModel { get; }

        public HTMLDocumentView()
        {
            this.InitializeComponent();

            ViewModel = (Application.Current as App)!.Host!.Services.GetRequiredService<HTMLDocumentViewModel>();

            ViewModel.RefreshRequested += ViewModel_RefreshRequested;
        }

        private void ViewModel_RefreshRequested()
        {
            if (HTMLView.CoreWebView2 != null)
            {
                HTMLView.CoreWebView2.Reload();
            }
        }

        public IDocument Document
        {
            get => ViewModel.Document;
            set => ViewModel.Document = value;
        }

        public void CloseDocument()
        {
            ViewModel.CloseDocumentCommand.Execute(null);
        }

        public async Task<Result> LoadDocumentAsync()
        {
            return await ViewModel.LoadDocumentAsync();
        }

        private void OnNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            // Log.Information($"Navigation starting: {args.Uri}");
        }

        private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            // Log.Information($"Navigation completed: {args}");
        }
    }
}
