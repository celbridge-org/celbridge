using Celbridge.Documents.ViewModels;

namespace Celbridge.Documents.Views;

public partial class TextDocumentView : TabViewItem
{
    public TextDocumentViewModel ViewModel { get; }

    public TextDocumentView()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<TextDocumentViewModel>();
    }
}
