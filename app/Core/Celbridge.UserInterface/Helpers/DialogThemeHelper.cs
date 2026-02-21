namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Helper for enabling automatic theme updates on ContentDialog instances.
/// </summary>
public static class DialogThemeHelper
{
    /// <summary>
    /// An extension method that enables automatic theme synchronization for a ContentDialog.
    /// Sets the initial theme immediately and updates RequestedTheme whenever ThemeChangedMessage is sent.
    /// </summary>
    public static void EnableThemeSync(this ContentDialog dialog)
    {
        var messengerService = ServiceLocator.AcquireService<IMessengerService>();

        // Set initial theme
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        dialog.RequestedTheme = userInterfaceService.UserInterfaceTheme == UserInterfaceTheme.Dark
            ? ElementTheme.Dark
            : ElementTheme.Light;

        void Register()
        {
            messengerService.Register<ThemeChangedMessage>(dialog, OnThemeChanged);
        }

        void Unregister()
        {
            messengerService.UnregisterAll(dialog);
        }

        void OnThemeChanged(object recipient, ThemeChangedMessage message)
        {
            dialog.RequestedTheme = message.Theme == UserInterfaceTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }

        // Register when the dialog's visual tree is loaded
        dialog.Loaded += (s, e) => Register();

        // Unregister on both Unloaded and Closed to ensure cleanup.
        // ContentDialog may not fire Unloaded reliably since it renders in a popup overlay,
        // so Closed provides a guaranteed cleanup point to prevent dangling references.
        dialog.Unloaded += (s, e) => Unregister();
        dialog.Closed += (s, e) => Unregister();
    }
}
