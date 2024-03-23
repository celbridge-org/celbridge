namespace Celbridge.BaseLibrary.UserInterface;

public interface IFilePickerService
{
    Task<Result<string>> PickSingleFileAsync(IEnumerable<string> fileExtensions);
    Task<Result<string>> PickSingleFolderAsync();
}
