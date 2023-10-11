using Celbridge.Services;
using Celbridge.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Celbridge.ViewModels
{
    public partial class RightNavigationBarViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly IStringLocalizer _localizer;

        public RightNavigationBarViewModel(ISettingsService settingsService, IStringLocalizer localizer)
        {
            _settingsService = settingsService;
            _localizer = localizer;
        }

        public XamlRoot ShellRoot { get; set; }

        public ICommand ToggleInspectorCommand => new RelayCommand(ToggleInspector_Executed);

        private void ToggleInspector_Executed()
        {
            // Toggle the inspector panel
            _settingsService.EditorSettings.RightPanelExpanded = !_settingsService.EditorSettings.RightPanelExpanded;
        }
    }
}
