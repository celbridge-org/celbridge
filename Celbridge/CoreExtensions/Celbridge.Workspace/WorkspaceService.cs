using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Documents;
using Celbridge.BaseLibrary.Inspector;
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

    public IProjectService ProjectService { get; }
    public IStatusService StatusService { get; }
    public IConsoleService ConsoleService { get; }
    public IInspectorService InspectorService { get; }
    public IDocumentsService DocumentsService { get; }

    public WorkspaceService(IServiceProvider serviceProvider)
    {
        // Create instances of the required sub-services
        ProjectService = serviceProvider.GetRequiredService<IProjectService>();
        StatusService = serviceProvider.GetRequiredService<IStatusService>();
        ConsoleService = serviceProvider.GetRequiredService<IConsoleService>();
        InspectorService = serviceProvider.GetRequiredService<IInspectorService>();
        DocumentsService = serviceProvider.GetRequiredService<IDocumentsService>();
    }
}
