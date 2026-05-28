using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DefaultDocumentViewModel : DocumentViewModel
{
    [ObservableProperty]
    private string _text = string.Empty;

    public async Task<Result> LoadDocument()
    {
        PropertyChanged -= TextDocumentViewModel_PropertyChanged;

        var loadResult = await LoadTextFromFileAsync();
        if (loadResult.IsFailure)
        {
            return Result.Fail($"Failed to load document file: '{FilePath}'")
                .WithErrors(loadResult);
        }
        Text = loadResult.Value;

        PropertyChanged += TextDocumentViewModel_PropertyChanged;

        return Result.Ok();
    }

    public async Task<Result> SaveDocumentContent()
    {
        // Don't immediately try to save again if the save fails.
        HasUnsavedChanges = false;
        SaveTimer = 0;

        return await SaveTextToFileAsync(Text);
    }

    private void TextDocumentViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Text))
        {
            OnDataChanged();
        }
    }
}
