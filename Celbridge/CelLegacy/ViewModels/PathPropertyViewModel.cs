namespace Celbridge.Legacy.ViewModels;

public partial class PathPropertyViewModel : ClassPropertyViewModel<string>
{
    public ICommand PickFileCommand => new AsyncRelayCommand(PickFile_ExecutedAsync);

    private async Task PickFile_ExecutedAsync()
    {
        var result = await FileUtils.ShowFileOpenPicker();
        if (result is ErrorResult<string> error)
        {
            Log.Error(error.Message);
            return;
        }
        
        Value = result.Data;
    }
}
