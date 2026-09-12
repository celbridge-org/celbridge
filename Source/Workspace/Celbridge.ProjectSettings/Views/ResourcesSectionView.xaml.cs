using Celbridge.ProjectSettings.ViewModels;

namespace Celbridge.ProjectSettings.Views;

/// <summary>
/// The Resources section of the Project Settings: the patterns the Explorer hides and the patterns search
/// skips, each edited as a block of glob patterns, one per line.
/// </summary>
public sealed partial class ResourcesSectionView : UserControl
{
    private ResourcesSectionViewModel? _viewModel;

    // Supplied by the panel that owns this section. Assigning it refreshes the bindings so the section
    // populates once the panel hands over its instance.
    public ResourcesSectionViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            Bindings?.Update();
        }
    }

    public ResourcesSectionView()
    {
        InitializeComponent();
    }
}
