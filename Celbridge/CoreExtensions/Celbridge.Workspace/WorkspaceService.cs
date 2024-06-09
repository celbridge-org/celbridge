using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Documents;
using Celbridge.BaseLibrary.Inspector;
using Celbridge.BaseLibrary.Messaging;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Status;
using Celbridge.BaseLibrary.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Workspace;

public class WorkspaceService : IWorkspaceService
{
    public bool IsLeftPanelVisible { get; }
    public bool IsRightPanelVisible { get; }
    public bool IsBottomPanelVisible { get; }

    private readonly IServiceProvider _serviceProvider;
    private readonly IMessengerService _messengerService;

    public IProjectService ProjectService { get; }
    public IStatusService StatusService { get; }
    public IConsoleService ConsoleService { get; }
    public IInspectorService InspectorService { get; }
    public IDocumentsService DocumentsService { get; }

    public WorkspaceService(IServiceProvider serviceProvider,
        IMessengerService messengerService)
    {
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;

        // Create instances of the required sub-services
        ProjectService = _serviceProvider.GetRequiredService<IProjectService>();
        StatusService = _serviceProvider.GetRequiredService<IStatusService>();
        ConsoleService = _serviceProvider.GetRequiredService<IConsoleService>();
        InspectorService = _serviceProvider.GetRequiredService<IInspectorService>();
        DocumentsService = _serviceProvider.GetRequiredService<IDocumentsService>();
    }
}
