using Celbridge.ProjectSettings.ViewModels;
using Celbridge.UserInterface;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.Views;

public sealed partial class InformationSectionView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private InformationSectionViewModel? _viewModel;

    // Supplied by the panel that owns this section. Assigning it refreshes the bindings so the section
    // populates once the panel hands over its instance.
    public InformationSectionViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            Bindings?.Update();
        }
    }

    public string SchemaVersionLabel => _stringLocalizer.GetString("ProjectSettings_SchemaVersionLabel");
    public string SchemaVersionTooltip => _stringLocalizer.GetString("ProjectSettings_SchemaVersionTooltip");
    public string ProjectVersionLabel => _stringLocalizer.GetString("ProjectSettings_ProjectVersionLabel");
    public string ProjectVersionTooltip => _stringLocalizer.GetString("ProjectSettings_ProjectVersionTooltip");
    public string IgnoreFileLabel => _stringLocalizer.GetString("ProjectSettings_IgnoreFileLabel");
    public string IgnoreFileTooltip => _stringLocalizer.GetString("ProjectSettings_IgnoreFileTooltip");

    public InformationSectionView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        InitializeComponent();
    }
}
