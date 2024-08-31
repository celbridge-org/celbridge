using Celbridge.Documents.ViewModels;

namespace Celbridge.Documents.Views;

// I've tried writing this class using a C# Markup class subclassed from TabViewItem, but it didn't work.
// No matter how simple I make the derived class, an exception is thrown when the class is instantiated.
// I've given up for now and am using a XAML file instead.
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
