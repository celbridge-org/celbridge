using Celbridge.ProjectSettings.ViewModels;

namespace Celbridge.ProjectSettings.Views;

public sealed partial class PagesSectionView : UserControl
{
    private PagesSectionViewModel? _viewModel;

    // Supplied by the panel that owns this section. Assigning it refreshes the bindings so the section
    // populates once the panel hands over its instance.
    public PagesSectionViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            Bindings?.Update();
        }
    }

    public PagesSectionView()
    {
        InitializeComponent();
    }
}
