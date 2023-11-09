using Celbridge.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Celbridge.Views
{
    public sealed partial class CelCanvas : UserControl
    {
        public CelCanvasViewModel ViewModel { get; set; }

        public CelCanvas(int x, int y)
        {
            InitializeComponent();

            // This is a bit of a hack, but we need to set the position of the view at start.
            ViewModel_CelPositionChanged(x, y);

            ViewModel = (Application.Current as App)!.Host!.Services.GetRequiredService<CelCanvasViewModel>();
            ViewModel.CelPositionChanged += ViewModel_CelPositionChanged;
        }

        private void CelCanvas_ManipulationStarted(object? sender, ManipulationStartedRoutedEventArgs e)
        {
            // Surpress tooltip until next time the cel is selected.
            // This prevents the tooltip from showing while the cel is moving.
            ToolTipService.SetToolTip(CelGrid, null);
        }

        private void CelCanvas_ManipulationDelta(object? sender, ManipulationDeltaRoutedEventArgs e)
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

        private void CelCanvas_ManipulationCompleted(object? sender, ManipulationCompletedRoutedEventArgs e)
        {
            // Restore the tooltip once movement has finished.
            ViewModel.UpdateTooltipText();
        }

        private void ViewModel_CelPositionChanged(int x, int y)
        {
            Canvas.SetLeft(this, x);
            Canvas.SetTop(this, y);
        }

        private void CelCanvas_Tapped(object? sender, TappedRoutedEventArgs e)
        {
            ViewModel.SelectCell();
        }
    }
}
