using Celbridge.Resources;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentTabViewModel : ObservableObject
{
    [ObservableProperty]
    public string _name = "Default";

    public ResourceKey FileResource { get; set; }
    public string FilePath { get; set; } = string.Empty;

    public IDocumentView? DocumentView { get; set; }

    /// <summary>
    /// Close the opened document.
    /// Returns false if the close operation was cancelled, e.g. via a confirmation dialog.
    /// The call fails if the close operation failed due to an error.
    /// </summary>
    public async Task<Result<bool>> CloseDocument()
    {
        Guard.IsNotNull(DocumentView);

        var canClose = await DocumentView.CanCloseDocument();
        if (!canClose)
        {
            // The close operation was cancelled by the document view.
            return Result<bool>.Ok(false);
        }

        if (DocumentView.IsDirty)
        {
            var saveResult = await DocumentView.SaveDocument();
            if (saveResult.IsFailure)
            {
                var failure = Result<bool>.Fail($"Saving document failed for file resource: '{FileResource}'");
                failure.MergeErrors(saveResult);
                return failure;
            }
        }

        return Result<bool>.Ok(true);
    }
}
