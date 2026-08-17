using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Projects;
using Celbridge.Reports;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// States the health of the current project's load beside the Project Switcher, and opens the load
/// report on click. Present only while the load found something worth acting on.
/// </summary>
public sealed partial class ProjectHealthButton : UserControl
{
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly IProjectHealthService _projectHealthService;

    public ProjectHealthButton()
    {
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _projectHealthService = ServiceLocator.AcquireService<IProjectHealthService>();

        this.InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _messengerService.Register<ProjectHealthChangedMessage>(this, OnProjectHealthChanged);

        UpdateHealth();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;

        _messengerService.UnregisterAll(this);
    }

    private void OnProjectHealthChanged(object recipient, ProjectHealthChangedMessage message)
    {
        UpdateHealth();
    }

    private void UpdateHealth()
    {
        var health = _projectHealthService.CurrentHealth;
        var severity = health?.Severity ?? ReportSeverity.Info;

        if (health is null ||
            severity == ReportSeverity.Info)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        var isError = severity == ReportSeverity.Error;

        ErrorIcon.Visibility = isError ? Visibility.Visible : Visibility.Collapsed;
        WarningIcon.Visibility = isError ? Visibility.Collapsed : Visibility.Visible;

        var healthText = ComposeHealthText(health);
        ToolTipService.SetToolTip(HealthButton, healthText);
        ToolTipService.SetPlacement(HealthButton, PlacementMode.Bottom);
        AutomationProperties.SetName(HealthButton, healthText);

        Visibility = Visibility.Visible;
    }

    private string ComposeHealthText(ProjectLoadReportSummary health)
    {
        switch (health.Severity)
        {
            case ReportSeverity.Error:
                return health.IssueCount == 1
                    ? _stringLocalizer.GetString("ProjectHealth_Errors_One")
                    : _stringLocalizer.GetString("ProjectHealth_Errors_Many", health.IssueCount);

            case ReportSeverity.Warning:
                return health.IssueCount == 1
                    ? _stringLocalizer.GetString("ProjectHealth_Warnings_One")
                    : _stringLocalizer.GetString("ProjectHealth_Warnings_Many", health.IssueCount);

            default:
                return _stringLocalizer.GetString("ProjectHealth_Healthy");
        }
    }

    private void HealthButton_Click(object sender, RoutedEventArgs e)
    {
        var reportResource = _projectHealthService.CurrentHealth?.Resource ?? ResourceKey.Empty;
        if (reportResource.IsEmpty)
        {
            return;
        }

        var commandService = ServiceLocator.AcquireService<ICommandService>();
        commandService.Execute<IOpenDocumentCommand>(command => command.FileResource = reportResource);
    }
}
