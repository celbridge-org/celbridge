using CommunityToolkit.Mvvm.ComponentModel;
using System;
using Windows.UI;

namespace Celbridge.ViewModels
{
    public partial class CelConnectionLineViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isHighlighted;

        [ObservableProperty]
        private Color _lineColor;

        public CelConnectionLineViewModel()
        {
            PropertyChanged += CelConnectionLineViewModel_PropertyChanged;

            LineColor = (Color)Application.Current.Resources["CelConnectionInactiveColor"];
        }

        private void CelConnectionLineViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IsHighlighted))
            {
                if (IsHighlighted)
                {
                    LineColor = (Color)Application.Current.Resources["CelConnectionActiveColor"];
                }
                else
                {
                    LineColor = (Color)Application.Current.Resources["CelConnectionInactiveColor"];
                }
            }
        }
    }
}
