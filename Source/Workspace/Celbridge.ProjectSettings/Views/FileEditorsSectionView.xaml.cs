using Celbridge.ProjectSettings.ViewModels;

namespace Celbridge.ProjectSettings.Views;

public sealed partial class FileEditorsSectionView : UserControl
{
    private FileEditorsSectionViewModel? _viewModel;

    // Supplied by the panel that owns this section. Assigning it refreshes the bindings so the section
    // populates once the panel hands over its instance.
    public FileEditorsSectionViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            Bindings?.Update();
        }
    }

    public FileEditorsSectionView()
    {
        InitializeComponent();
    }
}
