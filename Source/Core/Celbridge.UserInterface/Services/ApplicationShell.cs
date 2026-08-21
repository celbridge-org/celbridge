using Celbridge.Logging;
using Celbridge.UserInterface.Views;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

public class ApplicationShell : IApplicationShell
{
    private readonly ILogger<ApplicationShell> _logger;
    private readonly IServiceProvider _serviceProvider;

    private Panel? _contentArea;
    private IWorkspaceView? _workspaceView;
    private HomePage? _homePage;

    public ApplicationShell(
        ILogger<ApplicationShell> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gives the shell the area it shows the base view in, and shows Home. Called by MainPage once its
    /// layout has been built.
    /// </summary>
    public void SetContentArea(Panel contentArea)
    {
        Guard.IsNull(_contentArea);

        _contentArea = contentArea;

        ShowHome();
    }

    public async Task<Result> ShowWorkspaceAsync(CancellationTokenSource loadCancellation)
    {
        await Task.CompletedTask;

        if (_contentArea is null)
        {
            return Result.Fail("Failed to show the workspace because the shell has no content area.");
        }

        if (_workspaceView is not null)
        {
            return Result.Fail("Failed to show the workspace because a workspace is already showing.");
        }

        var workspaceView = _serviceProvider.GetRequiredService<IWorkspaceView>();

        var viewElement = workspaceView as UIElement;
        if (viewElement is null)
        {
            return Result.Fail($"Failed to show the workspace because the view type '{workspaceView.GetType()}' is not a UIElement.");
        }

        // Assigned before the view enters the visual tree, because the workspace load starts from its
        // Loaded event.
        workspaceView.LoadCancellation = loadCancellation;
        _workspaceView = workspaceView;

        HideHome();

        _contentArea.Children.Add(viewElement);

        return Result.Ok();
    }

    public async Task<Result> CloseWorkspaceAsync()
    {
        if (_workspaceView is null)
        {
            // No workspace is showing, so the shell is already in the requested state.
            return Result.Ok();
        }

        var workspaceView = _workspaceView;
        _workspaceView = null;

        // Teardown runs while the view is still in the visual tree, so the editors it saves state from are
        // still alive.
        var teardownResult = Result.Ok();
        try
        {
            await workspaceView.TeardownAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to tear down the workspace");
            teardownResult = Result.Fail("Failed to tear down the workspace");
        }

        if (workspaceView is UIElement viewElement)
        {
            _contentArea?.Children.Remove(viewElement);
        }

        ShowHome();

        return teardownResult;
    }

    private void ShowHome()
    {
        Guard.IsNotNull(_contentArea);

        if (_homePage is not null)
        {
            return;
        }

        // Home reads the recent projects as it is built, so it is rebuilt each time rather than kept and
        // shown again.
        var homePage = new HomePage();
        _contentArea.Children.Add(homePage);
        _homePage = homePage;
    }

    private void HideHome()
    {
        if (_homePage is null)
        {
            return;
        }

        _contentArea?.Children.Remove(_homePage);
        _homePage = null;
    }
}
