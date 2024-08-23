using System.Runtime.InteropServices;

namespace Celbridge.MainApplication;

#if DEBUG && WINDOWS

public class ConsoleHelper
{
    /// <summary>
    /// Allocates a new console window for current process.
    /// </summary>
    [DllImport("kernel32.dll")]
    public static extern Boolean AllocConsole();

    /// <summary>
    /// Frees the console window.
    /// </summary>
    [DllImport("kernel32.dll")]
    public static extern Boolean FreeConsole();
}

#endif