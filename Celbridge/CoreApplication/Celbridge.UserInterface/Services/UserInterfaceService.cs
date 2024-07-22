using Celbridge.UserInterface;
using Celbridge.Dialog;
using Celbridge.FilePicker;

namespace Celbridge.UserInterface.Services;

public class UserInterfaceService : IUserInterfaceService
{
    private IMessengerService _messengerService;

    private Window? _mainWindow;
    private XamlRoot? _xamlRoot;
    public object MainWindow => _mainWindow!;
    public object XamlRoot => _xamlRoot!;

    //
    // These properties provide convenient access to various user interface related services
    //
    public IFilePickerService FilePickerService { get; private set; }
    public IDialogService DialogService { get; private set;  }

    public UserInterfaceService(
        IMessengerService messengerService, 
        IFilePickerService filePickerService,
        IDialogService dialogService)
    {
        _messengerService = messengerService;
        FilePickerService = filePickerService;
        DialogService = dialogService;
    }

    public void Initialize(Window mainWindow, XamlRoot xamlRoot)
    {
        // Ensure these are only set once
        Guard.IsNull(_mainWindow);
        Guard.IsNull(_xamlRoot);

        _mainWindow = mainWindow;
        _xamlRoot = xamlRoot;

#if WINDOWS
        // Broadcast a message whenever the main window acquires or loses focus (Windows only).
        _mainWindow.Activated += MainWindow_Activated;
#endif
    }

#if WINDOWS
    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        var activationState = e.WindowActivationState;

        if (activationState == WindowActivationState.Deactivated)
        {
            var message = new MainWindowDeactivatedMessage();
            _messengerService.Send(message);
        }
        else if (activationState == WindowActivationState.PointerActivated ||
                 activationState == WindowActivationState.CodeActivated)
        {
            var message = new MainWindowActivatedMessage();
            _messengerService.Send(message);
        }
    }
#endif
}
