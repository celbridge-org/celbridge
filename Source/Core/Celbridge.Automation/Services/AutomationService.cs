using Celbridge.UserInterface;

namespace Celbridge.Automation.Services;

internal class AutomationService : IAutomationService
{
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly INativeControlReader _nativeControlReader;

    public AutomationService(
        IUserInterfaceService userInterfaceService,
        INativeControlReader nativeControlReader)
    {
        _userInterfaceService = userInterfaceService;
        _nativeControlReader = nativeControlReader;
    }

    public Task<Result<ControlSnapshot>> GetControlsAsync()
    {
        return RunOnUIThreadAsync(GetControls);
    }

    public Task<Result<ControlInvocation>> InvokeControlAsync(Func<ControlInfo, bool> match)
    {
        return RunOnUIThreadAsync(() => InvokeControl(match));
    }

    private Result<ControlSnapshot> GetControls()
    {
        if (_userInterfaceService.XamlRoot is not XamlRoot xamlRoot)
        {
            return Result.Fail("The application has no window content to read.");
        }

        var controls = new List<ControlInfo>();
        foreach (var showingControl in ReadControls(xamlRoot))
        {
            controls.Add(showingControl.Info);
        }

        var snapshot = new ControlSnapshot(
            controls,
            xamlRoot.Size.Width,
            xamlRoot.Size.Height,
            xamlRoot.RasterizationScale);

        return snapshot;
    }

    private Result<ControlInvocation> InvokeControl(Func<ControlInfo, bool> match)
    {
        if (_userInterfaceService.XamlRoot is not XamlRoot xamlRoot)
        {
            return Result.Fail("The application has no window content to search.");
        }

        foreach (var showingControl in ReadControls(xamlRoot))
        {
            var control = showingControl.Info;
            if (!control.IsEnabled ||
                !match(control))
            {
                continue;
            }

            var action = showingControl.PerformDefaultAction();
            if (action is null)
            {
                continue;
            }

            var invocation = new ControlInvocation(control, action.Value);
            return invocation;
        }

        return Result.Fail("No showing, enabled control that matches has a default action.");
    }

    private IEnumerable<ShowingControl> ReadControls(XamlRoot xamlRoot)
    {
        foreach (var showingControl in VisualTreeReader.ReadControls(xamlRoot, _nativeControlReader))
        {
            yield return showingControl;
        }

        foreach (var showingControl in _nativeControlReader.ReadControls())
        {
            yield return showingControl;
        }
    }

    // The visual tree belongs to the UI thread, and the caller is a tool request arriving on its own thread. The
    // dispatcher is used rather than the command queue, so a call still answers while a modal dialog holds the
    // queue.
    private Task<Result<T>> RunOnUIThreadAsync<T>(Func<Result<T>> operation) where T : notnull
    {
        if (_userInterfaceService.MainWindow is not Window mainWindow)
        {
            return Task.FromResult<Result<T>>(Result.Fail("The application has no main window to read."));
        }

        var dispatcherQueue = mainWindow.DispatcherQueue;
        if (dispatcherQueue.HasThreadAccess)
        {
            return Task.FromResult(operation());
        }

        var completionSource = new TaskCompletionSource<Result<T>>();
        var enqueued = dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                completionSource.TrySetResult(operation());
            }
            catch (Exception ex)
            {
                completionSource.TrySetResult(
                    Result<T>.Fail("The automation call failed on the UI thread").WithException(ex));
            }
        });

        if (!enqueued)
        {
            return Task.FromResult<Result<T>>(Result.Fail("Failed to dispatch the automation call to the UI thread."));
        }

        return completionSource.Task;
    }
}
