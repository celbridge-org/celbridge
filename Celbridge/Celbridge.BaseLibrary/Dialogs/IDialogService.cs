namespace Celbridge.BaseLibrary.Dialogs;

public interface IDialogService
{
    Task<Result<string>> ShowFileOpenPicker();
}
