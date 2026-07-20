using Celbridge.ProjectSettings.ViewModels;
using Celbridge.UserInterface;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.Views;

public sealed partial class FileEditorsSectionView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

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

    public string CategoryPickerTooltip => _stringLocalizer.GetString("ProjectSettings_CategoryPickerTooltip");
    public string FilterExtensionsPlaceholder => _stringLocalizer.GetString("ProjectSettings_FilterExtensionsPlaceholder");

    public FileEditorsSectionView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        InitializeComponent();
    }
}
