using Celbridge.UserInterface;
using Celbridge.WebView.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView.Views;

/// <summary>
/// The Home section of the Web View settings: the URL the document opens on, and the actions that adopt
/// the page on screen as that URL or as a new document.
/// </summary>
public sealed partial class WebViewHomeSectionView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private WebViewDocumentViewModel? _viewModel;

    // Supplied by the surface that owns this section. Assigning it refreshes the bindings so the section
    // populates once the surface hands over its instance.
    public WebViewDocumentViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            Bindings?.Update();
        }
    }

    public string HomeUrlLabelString => _stringLocalizer.GetString("WebView_Settings_HomeUrlLabel");
    public string AddressPlaceholderString => _stringLocalizer.GetString("WebView_UrlBar_AddressPlaceholder");
    public string InvalidUrlString => _stringLocalizer.GetString("WebView_InvalidUrl");
    public string SetCurrentPageAsHomeString => _stringLocalizer.GetString("WebView_Settings_SetCurrentPageAsHome");

    public WebViewHomeSectionView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        InitializeComponent();
    }

    private void SetCurrentPageAsHomeButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetCurrentPageAsHome();
    }
}
