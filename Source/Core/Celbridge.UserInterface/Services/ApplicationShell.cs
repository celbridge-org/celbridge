using Celbridge.Logging;
using Celbridge.UserInterface.Views;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// The workspace the shell is showing, paired with the element it is added to the content area as.
/// </summary>
internal sealed record CurrentWorkspace(IWorkspaceView View, UIElement Element);

public class ApplicationShell : IApplicationShell
{
    private readonly ILogger<ApplicationShell> _logger;
    private readonly IServiceProvider _serviceProvider;

    private Panel? _contentArea;
    private CurrentWorkspace? _currentWorkspace;
    private HomeView? _homeView;

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

        if (_currentWorkspace is not null)
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
        _currentWorkspace = new CurrentWorkspace(workspaceView, viewElement);

        HideHome();

        _contentArea.Children.Add(viewElement);

        return Result.Ok();
    }

    public async Task<Result> CloseWorkspaceAsync()
    {
        if (_currentWorkspace is null)
        {
            // No workspace is showing, so the shell is already in the requested state.
            return Result.Ok();
        }

        var currentWorkspace = _currentWorkspace;
        _currentWorkspace = null;

        // Teardown runs while the view is still in the visual tree, so the editors it saves state from are
        // still alive. The view comes out whether it succeeded or not: the caller is unloading the project
        // the workspace belongs to, so leaving it on screen would be worse than losing what it failed to save.
        var teardownResult = Result.Ok();
        try
        {
            await currentWorkspace.View.TeardownAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to tear down the workspace");
            teardownResult = Result.Fail("Failed to tear down the workspace");
        }

        Guard.IsNotNull(_contentArea);
        _contentArea.Children.Remove(currentWorkspace.Element);

        ShowHome();

        return teardownResult;
    }

    private void ShowHome()
    {
        Guard.IsNotNull(_contentArea);

        if (_homeView is not null)
        {
            return;
        }

        // Home reads the recent projects as it is built, so it is rebuilt each time rather than kept and
        // shown again.
        var homeView = new HomeView();
        _contentArea.Children.Add(homeView);
        _homeView = homeView;
    }

    private void HideHome()
    {
        if (_homeView is null)
        {
            return;
        }

        Guard.IsNotNull(_contentArea);

        _contentArea.Children.Remove(_homeView);
        _homeView = null;
    }
}
