namespace Celbridge.BaseLibrary.Dialogs;

public interface IDialogService
{
    Task<Result<string>> PickSingleFileAsync(IEnumerable<string> fileExtensions);
    Task<Result<string>> PickSingleFolderAsync();
}
