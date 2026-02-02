using Celbridge.Inspector.ViewModels;
using Microsoft.Extensions.Localization;
using Windows.System;

namespace Celbridge.Inspector.Views;

public partial class WebInspector : UserControl, IInspector
{
    private readonly IStringLocalizer _stringLocalizer;

    public WebInspectorViewModel ViewModel { get; }

    private string StartURLString => _stringLocalizer.GetString("WebInspector_StartURL");
    private string AddressPlaceholderString => _stringLocalizer.GetString("WebInspector_AddressPlaceholder");
    private string NavigateTooltipString => _stringLocalizer.GetString("WebInspector_NavigateTooltip");
    private string RefreshTooltipString => _stringLocalizer.GetString("WebInspector_RefreshTooltip");
    private string GoBackTooltipString => _stringLocalizer.GetString("WebInspector_GoBackTooltip");
    private string GoForwardTooltipString => _stringLocalizer.GetString("WebInspector_GoForwardTooltip");

    public ResourceKey Resource
    {
        set => ViewModel.Resource = value;
        get => ViewModel.Resource;
    }

    public WebInspector()
    {
        this.InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<WebInspectorViewModel>();
    }

    private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.NavigateCommand.Execute(null);
            e.Handled = true;
        }
    }
}
