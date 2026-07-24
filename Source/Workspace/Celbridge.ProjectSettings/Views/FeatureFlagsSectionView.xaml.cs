using Celbridge.ProjectSettings.ViewModels;

namespace Celbridge.ProjectSettings.Views;

public sealed partial class FeatureFlagsSectionView : UserControl
{
    private FeatureFlagsSectionViewModel? _viewModel;

    // Supplied by the panel that owns this section. Assigning it refreshes the bindings so the section
    // populates once the panel hands over its instance.
    public FeatureFlagsSectionViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            Bindings?.Update();
        }
    }

    public FeatureFlagsSectionView()
    {
        InitializeComponent();
    }
}
