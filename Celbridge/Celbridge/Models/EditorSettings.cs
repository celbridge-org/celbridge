using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace Celbridge.Models
{
    public partial class EditorSettings : ObservableObject
    {
        [ObservableProperty]
        private ApplicationTheme _applicationTheme;

        [ObservableProperty]
        private string _lastEditedCelScript = string.Empty;

        [ObservableProperty]
        private bool _leftPanelExpanded = true;

        [ObservableProperty]
        private float _leftPanelWidth = 250;

        [ObservableProperty]
        private bool _rightPanelExpanded = true;

        [ObservableProperty]
        private float _rightPanelWidth = 250;

        [ObservableProperty]
        private bool _bottomPanelExpanded = true;

        [ObservableProperty]
        private float _bottomPanelHeight = 200;

        [ObservableProperty]
        private float _detailPanelHeight = 200;

        [ObservableProperty]
        private string _previousNewProjectFolder = string.Empty;

        [ObservableProperty]
        private string _previousActiveProjectPath = string.Empty;

        [ObservableProperty]
        private List<string> _previousOpenDocuments = new();

        [ObservableProperty]
        private string _openAIKey = string.Empty;

        [ObservableProperty]
        private string _sheetsAPIKey = string.Empty;

        public EditorSettings()
        {
            // Required for serialization.
        }
    }
}
