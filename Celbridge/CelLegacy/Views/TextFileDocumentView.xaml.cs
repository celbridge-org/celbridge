namespace CelLegacy.Views;

public partial class TextFileDocumentView : TabViewItem, IDocumentView
{
    public TextFileDocumentViewModel ViewModel { get; }

    public TextFileDocumentView()
    {
        this.InitializeComponent();

        ViewModel = LegacyServiceProvider.Services!.GetRequiredService<TextFileDocumentViewModel>();

        /*
         We should be able to do this in XAML, but it crashes the app!
        < TextBox.Resources >
            < SolidColorBrush x: Key = "TextControlBackgroundPointerOver" Color = "{StaticResource PanelBackgroundABrush}" />
            < SolidColorBrush x: Key = "TextControlBackgroundFocused" Color = "{StaticResource PanelBackgroundABrush}" />
        </ TextBox.Resources >
        */

        // Don't change background color on hover or focus. No IDE does this.
        var brush = (SolidColorBrush)Application.Current.Resources["PanelBackgroundABrush"];
        ContentTextBox.Resources["TextControlBackgroundPointerOver"] = brush;
        ContentTextBox.Resources["TextControlBackgroundFocused"] = brush;
    }

    public IDocument Document
    { 
        get => ViewModel.Document;
        set => ViewModel.Document = value;
    }

    public void CloseDocument()
    {
        ViewModel.CloseDocumentCommand.ExecuteAsync(null);
    }

    public async Task<Result> LoadDocumentAsync()
    {
        return await ViewModel.LoadDocumentAsync();
    }
}
