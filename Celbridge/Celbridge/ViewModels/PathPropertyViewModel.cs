using Celbridge.Models;
using Celbridge.Utils;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;
using System.Windows.Input;
using Serilog;

namespace Celbridge.ViewModels
{
    public partial class PathPropertyViewModel : ClassPropertyViewModel<string>
    {
        public ICommand PickFileCommand => new AsyncRelayCommand(PickFile_ExecutedAsync);

        private async Task PickFile_ExecutedAsync()
        {
            var result = await FileUtils.ShowFileOpenPicker();
            if (result.Success)
            {
                Value = result.Data;
            }
            else
            {
                var error = result as ErrorResult<string>;
                Log.Error(error.Message);
            }
        }
    }
}
