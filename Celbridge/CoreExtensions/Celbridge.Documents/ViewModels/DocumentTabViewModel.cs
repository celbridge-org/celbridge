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
    /// forceClose forces the document to close without allowing the document to cancel the close operation.
    /// Returns false if the document cancelled the close operation, e.g. via a confirmation dialog.
    /// The call fails if the close operation failed due to an error.
    /// </summary>
    public async Task<Result<bool>> CloseDocument(bool forceClose)
    {
        Guard.IsNotNull(DocumentView);

        if (!File.Exists(FilePath))
        {
            // The file no longer exists, so we presume that it was deleted intentionally.
            // Any pending save changes are discarded.
            return Result<bool>.Ok(true);
        }

        var canClose = forceClose || await DocumentView.CanCloseDocument();
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
