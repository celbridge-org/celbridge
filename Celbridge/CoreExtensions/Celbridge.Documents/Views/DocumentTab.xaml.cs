using Celbridge.Documents.ViewModels;

namespace Celbridge.Documents.Views;

public partial class DocumentTab : TabViewItem
{
    public DocumentTabViewModel ViewModel { get; }

    public DocumentTab()
    {
        this.InitializeComponent();

        var serviceProvider = ServiceLocator.ServiceProvider;
        ViewModel = serviceProvider.GetRequiredService<DocumentTabViewModel>();
    }
}
