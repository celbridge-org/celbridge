using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Documents;
using Celbridge.BaseLibrary.Inspector;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Status;
using Celbridge.BaseLibrary.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Workspace;

public class WorkspaceService : IWorkspaceService, IDisposable
{
    public bool IsLeftPanelVisible { get; }
    public bool IsRightPanelVisible { get; }
    public bool IsBottomPanelVisible { get; }

    public IConsoleService ConsoleService { get; }
    public IDocumentsService DocumentsService { get; }
    public IInspectorService InspectorService { get; }
    public IProjectService ProjectService { get; }
    public IStatusService StatusService { get; }

    public WorkspaceService(IServiceProvider serviceProvider)
    {
        // Create instances of the required sub-services
        ConsoleService = serviceProvider.GetRequiredService<IConsoleService>();
        DocumentsService = serviceProvider.GetRequiredService<IDocumentsService>();
        InspectorService = serviceProvider.GetRequiredService<IInspectorService>();
        ProjectService = serviceProvider.GetRequiredService<IProjectService>();
        StatusService = serviceProvider.GetRequiredService<IStatusService>();
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // We use the dispose pattern to ensure that the sub-services release all their resources when the project is closed.
                // This helps avoid memory leaks and orphaned objects/tasks when the user edits multiple projects during a session.

                (ConsoleService as IDisposable)!.Dispose();
                (DocumentsService as IDisposable)!.Dispose();
                (InspectorService as IDisposable)!.Dispose();
                (ProjectService as IDisposable)!.Dispose();
                (StatusService as IDisposable)!.Dispose();
            }

            _disposed = true;
        }
    }

    ~WorkspaceService()
    {
        Dispose(false);
    }
}
