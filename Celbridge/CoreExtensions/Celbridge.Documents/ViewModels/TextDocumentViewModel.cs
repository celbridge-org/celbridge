using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class TextDocumentViewModel : DocumentViewModel
{
    private string _filePath = string.Empty;

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

            _filePath = filePath;

            PropertyChanged += TextDocumentViewModel_PropertyChanged;
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"Failed to read file contents: '{filePath}'");
        }

        return Result.Ok();
    }

    public async Task<Result> SaveDocument()
    {
        // Don't immediately try to save again if the save fails.
        IsDirty = false;

        try
        {
            await File.WriteAllTextAsync(_filePath, Text);
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"Failed to write file contents: '{_filePath}'");
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
