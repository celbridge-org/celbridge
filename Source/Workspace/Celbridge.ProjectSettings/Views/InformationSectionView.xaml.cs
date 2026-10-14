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

    public string CelbridgeVersionLabel => _stringLocalizer.GetString("ProjectSettings_CelbridgeVersionLabel");
    public string CelbridgeVersionHint => _stringLocalizer.GetString("ProjectSettings_CelbridgeVersionHint");
    public string ProjectVersionLabel => _stringLocalizer.GetString("ProjectSettings_ProjectVersionLabel");
    public string ProjectVersionHint => _stringLocalizer.GetString("ProjectSettings_ProjectVersionHint");
    public string InvalidProjectVersionText => _stringLocalizer.GetString("ProjectSettings_InvalidProjectVersion");
    public string DescriptionLabel => _stringLocalizer.GetString("ProjectSettings_DescriptionLabel");
    public string DescriptionHint => _stringLocalizer.GetString("ProjectSettings_DescriptionHint");
    public string OpenProjectDataText => _stringLocalizer.GetString("ProjectSettings_OpenProjectData");
    public string OpenProjectDataHint => _stringLocalizer.GetString("ProjectSettings_OpenProjectDataHint");
    public string ViewLoadReportText => _stringLocalizer.GetString("ProjectSettings_ViewLoadReport");
    public string ViewLoadReportHint => _stringLocalizer.GetString("ProjectSettings_ViewLoadReportHint");

    public InformationSectionView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        InitializeComponent();
    }
}
