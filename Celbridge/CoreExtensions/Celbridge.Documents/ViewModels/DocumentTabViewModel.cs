using Celbridge.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Documents.ViewModels;

public partial class DocumentTabViewModel : ObservableObject
{
    [ObservableProperty]
    public string _name = "Default";

    public ResourceKey ResourceKey { get; set; }
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Close the opened document.
    /// Returns false if the close operation was cancelled, e.g. via a confirmation dialog.
    /// The call fails if the close operation failed due to an error.
    /// </summary>
    public async Task<Result<bool>> CloseDocument()
    {
        // Todo: Close the wrapped document object - using the dispose pattern?

        await Task.CompletedTask;
        return Result<bool>.Ok(true);
    }
}
