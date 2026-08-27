using Celbridge.UserInterface;
using Celbridge.WebView.ViewModels;
using Microsoft.Extensions.Localization;

namespace Celbridge.WebView.Views;

/// <summary>
/// The Appearance section of the Web View settings: which chrome the document shows around the page.
/// </summary>
public sealed partial class WebViewAppearanceSectionView : UserControl
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

    public string ShowUrlBarLabelString => _stringLocalizer.GetString("WebView_Settings_ShowUrlBarLabel");
    public string ShowUrlBarHintString => _stringLocalizer.GetString("WebView_Settings_ShowUrlBarHint");
    public string ShowBookmarksBarLabelString => _stringLocalizer.GetString("WebView_Settings_ShowBookmarksBarLabel");
    public string ShowBookmarksBarHintString => _stringLocalizer.GetString("WebView_Settings_ShowBookmarksBarHint");

    public WebViewAppearanceSectionView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        InitializeComponent();
    }
}
