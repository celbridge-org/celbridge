using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DefaultDocumentViewModel : DocumentViewModel
{
    [ObservableProperty]
    private string _text = string.Empty;

    public async Task<Result> LoadDocument()
    {
        try
        {
            PropertyChanged -= TextDocumentViewModel_PropertyChanged;

            // Read the file contents to initialize the text editor
            var text = await File.ReadAllTextAsync(FilePath);
            Text = text;

            PropertyChanged += TextDocumentViewModel_PropertyChanged;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to load document file: '{FilePath}'")
                .WithException(ex);
        }

        return Result.Ok();
    }

    public async Task<Result> SaveDocument()
    {
        // Don't immediately try to save again if the save fails.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        try
        {
            await File.WriteAllTextAsync(FilePath, Text);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to save document file: '{FilePath}'")
                .WithException(ex);
        }

        return Result.Ok();
    }

    private void TextDocumentViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Text))
        {
            OnDataChanged();
        }
    }
}
