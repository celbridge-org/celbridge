using Celbridge.ProjectSettings.ViewModels;

namespace Celbridge.ProjectSettings.Views;

public sealed partial class PackagesSectionView : UserControl
{
    private PackagesSectionViewModel? _viewModel;

    // Supplied by the panel that owns this section. Assigning it refreshes the bindings so the section
    // populates once the panel hands over its instance.
    public PackagesSectionViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            Bindings?.Update();
        }
    }

    public PackagesSectionView()
    {
        InitializeComponent();
    }
}
