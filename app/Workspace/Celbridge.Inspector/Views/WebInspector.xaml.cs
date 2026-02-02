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
    private string OpenURLTooltipString => _stringLocalizer.GetString("WebInspector_OpenURLTooltip");

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
            // Force the currently entered text to be submitted
            var textBox = sender as TextBox;
            var bindingExpression = textBox?.GetBindingExpression(TextBox.TextProperty);
            bindingExpression?.UpdateSource();

            ViewModel.OpenDocumentCommand.Execute(this);
            e.Handled = true;
        }
    }
}
