using Celbridge.Inspector.ViewModels;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using Windows.System;

namespace Celbridge.Inspector.Views;

public partial class WebInspector : UserControl, IInspector
{
    private readonly IStringLocalizer _stringLocalizer;

    public WebInspectorViewModel ViewModel => (DataContext as WebInspectorViewModel)!;
    private LocalizedString StartURLString => _stringLocalizer.GetString("WebInspector_StartURL");
    private LocalizedString AddressPlaceholderString => _stringLocalizer.GetString("WebInspector_AddressPlaceholder");
    private LocalizedString OpenURLTooltipString => _stringLocalizer.GetString("WebInspector_OpenURLTooltip");

    private TextBox? _urlTextBox;

    public ResourceKey Resource 
    {
        set => ViewModel.Resource = value; 
        get => ViewModel.Resource; 
    }

    // Code gen requires a parameterless constructor
    public WebInspector()
    {
        throw new NotImplementedException();
    }

    public WebInspector(WebInspectorViewModel viewModel)
    {
        var serviceProvider = ServiceLocator.ServiceProvider;
        _stringLocalizer = serviceProvider.GetRequiredService<IStringLocalizer>();

        DataContext = viewModel;

        this.DataContext<WebInspectorViewModel>((inspector, vm) => inspector
            .Content
            (
                new StackPanel()
                    .Orientation(Orientation.Vertical)
                    .Children
                    (
                        new Grid()
                            .ColumnDefinitions("*, auto")
                            .HorizontalAlignment(HorizontalAlignment.Stretch)
                            .Children
                            (
                                new TextBox()
                                    .Grid(column: 0)
                                    .Name(out _urlTextBox)
                                    // Todo: Localize this header
                                    // I've tried everything I can think of here, but I can't get the binding to
                                    // display the localized string in StartURLString to work. I just get a cryptic
                                    // com exception with no extra information.
                                    // https://platform.uno/docs/articles/external/uno.extensions/doc/Learn/Markup/Binding101.html
                                    .Header("Start URL")
                                    .PlaceholderText(inspector.AddressPlaceholderString)
                                    .HorizontalAlignment(HorizontalAlignment.Stretch)
                                    .IsSpellCheckEnabled(false)
                                    .TextWrapping(TextWrapping.Wrap)
                                    .Text(x => x.Binding(() => vm.SourceUrl)
                                        .Mode(BindingMode.TwoWay)),
                                new Button()
                                    .Grid(column: 1)
                                    .Margin(2, 0, 2, 0)
                                    .VerticalAlignment(VerticalAlignment.Bottom)
                                    .Command(ViewModel.OpenDocumentCommand)
                                    .ToolTipService(null, null, inspector.OpenURLTooltipString)
                                    .IsEnabled(x => x.Binding(() => vm.SourceUrl)
                                        .Mode(BindingMode.OneWay)
                                        .Convert((url) => !string.IsNullOrWhiteSpace(url)))
                                    .Content
                                    (
                                        new SymbolIcon()
                                            .Symbol(Symbol.OpenFile)
                                    )
                            )
                    )
            ));

        Guard.IsNotNull(_urlTextBox);
        _urlTextBox.KeyDown += CommandTextBox_KeyDown;
    }

    private void CommandTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
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
