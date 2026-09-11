using Celbridge.UserInterface.Platform;
using Microsoft.UI.Xaml;

namespace Celbridge.UserInterface.Services;

public class InputSimulationService : IInputSimulationService
{
    private readonly IUserInterfaceService _userInterfaceService;

    public InputSimulationService(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
    }

    public Task<Result> PressKeyAsync(string key, string modifiers)
    {
        var command = false;
        var control = false;
        var shift = false;
        var option = false;

        foreach (var token in modifiers.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "command":
                    command = true;
                    break;
                case "control":
                    control = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "option":
                    option = true;
                    break;
                default:
                    return Task.FromResult<Result>(Result.Fail(
                        $"Unknown modifier '{token}'. Supported modifiers: command, control, shift, option."));
            }
        }

        return RunOnUIThreadAsync(() => MacOSInputSimulator.PressKey(key, command, control, shift, option));
    }

    // The simulator calls AppKit, which is main thread only, and the caller is a tool request arriving on
    // its own thread. The dispatcher is used rather than the command queue so the press still lands while
    // a modal dialog holds the queue.
    private Task<Result> RunOnUIThreadAsync(Func<Result> operation)
    {
        if (_userInterfaceService.MainWindow is not Window mainWindow)
        {
            return Task.FromResult<Result>(Result.Fail("The application has no main window to receive the input."));
        }

        var dispatcherQueue = mainWindow.DispatcherQueue;
        if (dispatcherQueue.HasThreadAccess)
        {
            return Task.FromResult(operation());
        }

        var completionSource = new TaskCompletionSource<Result>();

        var enqueued = dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                completionSource.TrySetResult(operation());
            }
            catch (Exception ex)
            {
                completionSource.TrySetResult(
                    Result.Fail("The simulated input failed on the UI thread").WithException(ex));
            }
        });

        if (!enqueued)
        {
            return Task.FromResult<Result>(Result.Fail("Failed to dispatch the simulated input to the UI thread."));
        }

        return completionSource.Task;
    }
}
