using Celbridge.Messaging;

namespace Celbridge.Projects.Services;

public class ProjectHealthService : IProjectHealthService
{
    private readonly IMessengerService _messengerService;

    public ProjectLoadReportSummary? CurrentHealth { get; private set; }

    public ProjectHealthService(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public void SetHealth(ProjectLoadReportSummary health)
    {
        CurrentHealth = health;

        var message = new ProjectHealthChangedMessage(health);
        _messengerService.Send(message);
    }

    public void ClearHealth()
    {
        if (CurrentHealth is null)
        {
            return;
        }

        CurrentHealth = null;

        var message = new ProjectHealthChangedMessage(null);
        _messengerService.Send(message);
    }
}
