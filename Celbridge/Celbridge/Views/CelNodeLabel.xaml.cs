using Celbridge.ViewModels;

namespace Celbridge.Views
{
    public sealed partial class CelNodeLabel : UserControl
    {
        public CelNodeViewModel? ViewModel { get; set; }

        public CelNodeLabel()
        {
            this.InitializeComponent();
        }

        public void OnCelPositionChanged(int x, int y)
        {
            Canvas.SetLeft(this, x);
            Canvas.SetTop(this, y);
        }
    }
}
