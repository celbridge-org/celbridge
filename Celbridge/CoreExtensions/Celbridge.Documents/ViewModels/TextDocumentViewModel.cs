using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class TextDocumentViewModel : DocumentViewModel
{
    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isDirty = false;

    public async Task<Result> LoadDocument(string filePath)
    {
        try
        {
            PropertyChanged -= TextDocumentViewModel_PropertyChanged;

            // Read the file contents to initialize the text editor
            var text = await File.ReadAllTextAsync(filePath);
            Text = text;

            PropertyChanged += TextDocumentViewModel_PropertyChanged;
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"Failed to read file contents: '{filePath}'");
        }

        return Result.Ok();
    }

    private void TextDocumentViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Text))
        {
            OnTextChanged();
        }
    }

    public void OnTextChanged()
    {
        IsDirty = true;
    }
}
