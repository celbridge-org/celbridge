using Celbridge.Models;
using Celbridge.Utils;
using Celbridge.ViewModels;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Celbridge.Views
{
    public interface IDocumentsPanelView
    {
        bool TryFocusDocumentTab(IDocument document);
        Result OpenDocumentTab(TabViewItem tabItem);
        void CloseDocumentTab(IDocument document);
    }

    public sealed partial class DocumentsPanel : UserControl, IDocumentsPanelView
    {
        public DocumentsViewModel ViewModel {get; set; }

        public DocumentsPanel()
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App)!.Host!.Services.GetRequiredService<DocumentsViewModel>();
            ViewModel.DocumentsPanelView = this;
        }

        public bool TryFocusDocumentTab(IDocument document)
        {
            foreach (var tabViewitem in DocumentTabView.TabItems)
            {
                var documentView = tabViewitem as IDocumentView;
                Guard.IsNotNull(documentView);

                if (documentView.Document == document)
                {
                    DocumentTabView.SelectedItem = tabViewitem;
                    return true;
                }
            }
            return false;
        }

        public Result OpenDocumentTab(TabViewItem tabItem)
        {
            Guard.IsNotNull(tabItem);

            DocumentTabView.TabItems.Add(tabItem);
            DocumentTabView.SelectedItem = tabItem;

            return new SuccessResult();
        }

        public void CloseDocumentTab(IDocument document)
        {
            TabViewItem? tabViewItem = null;
            foreach (var tab in DocumentTabView.TabItems)
            {
                var documentView = tab as IDocumentView;
                Guard.IsNotNull(documentView);

                if (documentView.Document == document)
                {
                    tabViewItem = tab as TabViewItem;
                    break;
                }
            }

            Guard.IsNotNull(tabViewItem);
            DocumentTabView.TabItems.Remove(tabViewItem);
        }

        private void DocumentTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            var tabViewItem = args.Item as TabViewItem;
            Guard.IsNotNull(tabViewItem);

            var documentView = tabViewItem as IDocumentView;
            Guard.IsNotNull(documentView);

            documentView.CloseDocument();
        }
    }
}
