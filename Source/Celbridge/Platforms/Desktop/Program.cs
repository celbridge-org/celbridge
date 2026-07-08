using Uno.UI.Hosting;

namespace Celbridge;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseMacOS()
            .UseWin32()
            .Build();

        // Add the macOS open-document handler to Uno's application delegate before the run loop starts. macOS
        // delivers a double-clicked file's open event before applicationDidFinishLaunching:, so installing it
        // from App.OnLaunched would be too late and AppKit would reject the file. No-op off macOS.
        Celbridge.UserInterface.Platform.MacOSFileActivation.InstallOnDelegateClass();

        host.Run();
    }
}
