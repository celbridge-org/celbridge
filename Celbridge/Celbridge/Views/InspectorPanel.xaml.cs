using Celbridge.Services;
using Celbridge.ViewModels;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Celbridge.Views
{
    public sealed partial class InspectorPanel : UserControl
    {
        private readonly ISettingsService _settings;

        public InspectorViewModel ViewModel { get; set; }

        public InspectorPanel()
        {
            this.InitializeComponent();

            var app = Application.Current as App;
            Guard.IsNotNull(app);

            ViewModel = app.Host!.Services.GetRequiredService<InspectorViewModel>();
            ViewModel.ItemCollection = PropertyListView.Items;

            _settings = app.Host!.Services.GetRequiredService<ISettingsService>();

            Loaded += InspectorPanel_Loaded;
        }

        private void InspectorPanel_Loaded(object? sender, RoutedEventArgs e)
        {
            Guard.IsNotNull(_settings.EditorSettings);
            var height = _settings.EditorSettings.DetailPanelHeight;
            DetailPanelRow.Height = new GridLength(height);
        }

        private void InspectorPanel_LayoutUpdated(object? sender, object e)
        {
            var height = (float)DetailPanelRow.Height.Value;

            // This gets called frequently so we're relying on the equality 
            // check in the setter to avoid unnecessary writes to the settings.
            Guard.IsNotNull(_settings.EditorSettings);
            _settings.EditorSettings.DetailPanelHeight = height;
        }
    }
}
