using Celbridge.ProjectSettings.ViewModels;
using Celbridge.UserInterface;
using Microsoft.Extensions.Localization;

namespace Celbridge.ProjectSettings.Views;

/// <summary>
/// The Safe Mode section of Project Settings. The section says why the project is in Safe Mode, and offers the fix
/// and the load report.
/// </summary>
public sealed partial class SafeModeSectionView : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    private ProjectSettingsEditorViewModel? _viewModel;

    // The panel that owns this section supplies the view model. Setting the view model refreshes the bindings, so
    // the section shows the view model's values.
    public ProjectSettingsEditorViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            Bindings?.Update();
        }
    }

    public string SafeModeCaptionText => _stringLocalizer.GetString("ProjectSettings_SafeModeCaption");
    public string UntrustedText => _stringLocalizer.GetString("ProjectSettings_SafeModeUntrusted");
    public string TrustProjectText => _stringLocalizer.GetString("ProjectSettings_TrustProject");
    public string ViewLoadReportText => _stringLocalizer.GetString("ProjectSettings_ViewLoadReport");
    public string ViewLoadReportTooltip => _stringLocalizer.GetString("ProjectSettings_ViewLoadReportTooltip");

    public SafeModeSectionView()
    {
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        InitializeComponent();
    }
}
