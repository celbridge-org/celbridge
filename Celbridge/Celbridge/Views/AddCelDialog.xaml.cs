using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Numerics;

namespace Celbridge.Views
{
    public sealed partial class AddCelDialog : ContentDialog
    {
        public AddCelViewModel ViewModel { get; set; }

        // Todo: Remove dependency on model here
        public AddCelDialog(Models.ICelScript celScript, Vector2 spawnPosition)
        {
            this.InitializeComponent();
            ViewModel = (Application.Current as App).Host.Services.GetRequiredService<AddCelViewModel>();

            ViewModel.CelScript = celScript;
            ViewModel.SpawnPosition = spawnPosition;
        }
    }
}
