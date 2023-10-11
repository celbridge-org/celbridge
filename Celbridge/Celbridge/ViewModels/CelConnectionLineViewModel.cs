using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace Celbridge.ViewModels
{
    public partial class CelConnectionLineViewModel : ObservableObject
    {
        public CelConnectionLineViewModel()
        {}

        [ObservableProperty]
        private float _strokeThickness;
    }
}
