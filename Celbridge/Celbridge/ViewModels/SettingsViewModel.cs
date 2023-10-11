using Celbridge.Models;
using Celbridge.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Celbridge.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;

        public SettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;

            ThemeValues = Enum.GetValues(typeof(ApplicationTheme)).Cast<ApplicationTheme>();
        }

        public IEnumerable<ApplicationTheme> ThemeValues { get; }

        public ApplicationTheme Theme 
        { 
            get
            {
                return _settingsService.EditorSettings.ApplicationTheme;
            }
            set 
            {
                // Wrap model property
                SetProperty(_settingsService.EditorSettings.ApplicationTheme, 
                    value,    
                    _settingsService.EditorSettings,
                    (s, t) => s.ApplicationTheme = t);
            }
        }
    }
}
