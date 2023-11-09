using Celbridge.ViewModels;
using Microsoft.UI.Xaml.Input;

namespace Celbridge.Views
{
    public sealed partial class CelNode : UserControl
    {
        public CelNodeViewModel ViewModel { get; set; }

        public CelNodeLabel CelNodeLabel { get; set; }

        public CelNode(int x, int y)
        {
            InitializeComponent();

            ViewModel = (Application.Current as App)!.Host!.Services.GetRequiredService<CelNodeViewModel>();

            // We manage the node and labels as separate canvases.
            // There must be a way to do this with a single canvas, but it's easy to control the layout this way.
            CelNodeLabel = new CelNodeLabel();
            CelNodeLabel.ViewModel = ViewModel;

            // Order is important here because we need to set the position of the node before we start listening for position changes.
            ViewModel_CelPositionChanged(x, y);
            ViewModel.CelPositionChanged += ViewModel_CelPositionChanged;
        }

        private void CelNode_ManipulationStarted(object? sender, ManipulationStartedRoutedEventArgs e)
        {
            // Surpress tooltip until next time the cel is selected.
            // This prevents the tooltip from showing while the cel is moving.
            ToolTipService.SetToolTip(CelGrid, null);
        }

        private void CelNode_ManipulationDelta(object? sender, ManipulationDeltaRoutedEventArgs e)
        {
            ViewModel.SelectCell();

            var uiElement = sender as UIElement;
            var left = Canvas.GetLeft(uiElement);
            var top = Canvas.GetTop(uiElement);

            // Apply delta offset
            left += e.Delta.Translation.X;
            top += e.Delta.Translation.Y;

            // There's no point storing these values as doubles.
            // The extra precision just bloats the file size for no visible benefit.
            var x = Convert.ToInt32(left);
            var y = Convert.ToInt32(top);

            ViewModel.SetCelPosition(x, y);
        }

        private void CelNode_ManipulationCompleted(object? sender, ManipulationCompletedRoutedEventArgs e)
        {
            // Restore the tooltip once movement has finished.
            ViewModel.UpdateTooltipText();
        }

        private void ViewModel_CelPositionChanged(int x, int y)
        {
            Canvas.SetLeft(this, x);
            Canvas.SetTop(this, y);

            CelNodeLabel.OnCelPositionChanged(x, y);
        }

        private void CelNode_Tapped(object? sender, TappedRoutedEventArgs e)
        {
            ViewModel.SelectCell();
        }
    }
}
