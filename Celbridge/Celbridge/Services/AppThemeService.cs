using Celbridge.ViewModels;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Text;

namespace Celbridge.Services
{
    public interface IAppThemeService
    {
        void ApplyTheme();
    }

    public class AppThemeService : IAppThemeService
    {
        private readonly ISettingsService _settingsService;

        public AppThemeService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void ApplyTheme()
        {
            // WinUI/Uno only supports setting the theme at application startup time.
            var applicationTheme = _settingsService.EditorSettings.ApplicationTheme;
#if HAS_UNO
            switch (applicationTheme)
            {
                case ApplicationTheme.Light:
                    Uno.UI.ApplicationHelper.RequestedCustomTheme = "Light";
                    break;
                case ApplicationTheme.Dark:
                    Uno.UI.ApplicationHelper.RequestedCustomTheme = "Dark";
                    break;
            }
#else
            switch (applicationTheme)
            {
                case ApplicationTheme.Light:
                    App.Current.RequestedTheme = ApplicationTheme.Light;
                    break;
                case ApplicationTheme.Dark:
                    App.Current.RequestedTheme = ApplicationTheme.Dark;
                    break;
            }
#endif
        }
    }
}
