namespace Celbridge.BaseLibrary.UserInterface.FilePicker;

public interface IFilePickerService
{
    Task<Result<string>> PickSingleFileAsync(IEnumerable<string> fileExtensions);
    Task<Result<string>> PickSingleFolderAsync();
}
