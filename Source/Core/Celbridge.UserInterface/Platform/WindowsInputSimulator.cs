using System.Runtime.InteropServices;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// One key message of a simulated press, as posted to the focused window.
/// </summary>
internal readonly record struct KeyMessage(uint Message, ushort VirtualKey, uint Parameter);

/// <summary>
/// Posts a synthetic key press to the window that holds the keyboard on the UI thread, so the press travels
/// the same route a real key press takes from the message queue onward: the window's message handling, the
/// focused control, and any focused web view. It never passes through the system input stream, which is
/// where a desktop automation watches for its own stop key. It exists for keys an external test harness
/// cannot deliver. Windows-only.
/// </summary>
internal static class WindowsInputSimulator
{
    private readonly record struct KeyMapping(ushort VirtualKey, byte ScanCode, bool IsExtended);

    private const uint MessageKeyDown = 0x0100;
    private const uint MessageKeyUp = 0x0101;

    // The fields of a key message's parameter: the repeat count, the scan code, the extended-key flag, and
    // for a release the previous-state and transition flags.
    private const uint RepeatCountOfOne = 0x0001;
    private const int ScanCodeShift = 16;
    private const uint ExtendedKeyFlag = 1u << 24;
    private const uint ReleaseFlags = (1u << 30) | (1u << 31);

    /// <summary>
    /// The keys that can be pressed, mapped to their virtual-key code, their scan code, and whether they sit
    /// in the extended block, which is what tells the arrows and navigation keys apart from their numeric
    /// keypad twins. An allowlist of non-printable keys by design: printable characters already reach the
    /// app through ordinary text input, so nothing is gained by synthesising them.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, KeyMapping> Keys =
        new Dictionary<string, KeyMapping>(StringComparer.OrdinalIgnoreCase)
        {
            ["Escape"] = new(0x1B, 0x01, false),
            ["Tab"] = new(0x09, 0x0F, false),
            ["Return"] = new(0x0D, 0x1C, false),
            ["Backspace"] = new(0x08, 0x0E, false),
            ["ForwardDelete"] = new(0x2E, 0x53, true),
            ["Home"] = new(0x24, 0x47, true),
            ["End"] = new(0x23, 0x4F, true),
            ["PageUp"] = new(0x21, 0x49, true),
            ["PageDown"] = new(0x22, 0x51, true),
            ["Up"] = new(0x26, 0x48, true),
            ["Down"] = new(0x28, 0x50, true),
            ["Left"] = new(0x25, 0x4B, true),
            ["Right"] = new(0x27, 0x4D, true),
            ["F1"] = new(0x70, 0x3B, false),
            ["F2"] = new(0x71, 0x3C, false),
            ["F3"] = new(0x72, 0x3D, false),
            ["F4"] = new(0x73, 0x3E, false),
            ["F5"] = new(0x74, 0x3F, false),
            ["F6"] = new(0x75, 0x40, false),
            ["F7"] = new(0x76, 0x41, false),
            ["F8"] = new(0x77, 0x42, false),
            ["F9"] = new(0x78, 0x43, false),
            ["F10"] = new(0x79, 0x44, false),
            ["F11"] = new(0x7A, 0x57, false),
            ["F12"] = new(0x7B, 0x58, false),
        };

    /// <summary>
    /// The key names this simulator accepts, in declaration order, for error messages and the guide.
    /// </summary>
    public static IReadOnlyList<string> SupportedKeys { get; } = Keys.Keys.ToList();

    /// <summary>
    /// The key-down and key-up messages for a press, in the order they are posted. Fails when the key name is
    /// not one this simulator knows, or when a modifier is asked for, since a posted message does not change
    /// the modifier state the application reads.
    /// </summary>
    public static Result<IReadOnlyList<KeyMessage>> PlanKeyMessages(
        string key,
        bool command,
        bool control,
        bool shift,
        bool option)
    {
        if (command || control || shift || option)
        {
            return Result<IReadOnlyList<KeyMessage>>.Fail(
                "Modifiers are not supported on Windows. Send a chord with the desktop automation instead.");
        }

        if (!Keys.TryGetValue(key, out var mapping))
        {
            return Result<IReadOnlyList<KeyMessage>>.Fail(
                $"Unknown key '{key}'. Supported keys: {string.Join(", ", SupportedKeys)}.");
        }

        var pressParameter = RepeatCountOfOne | ((uint)mapping.ScanCode << ScanCodeShift);
        if (mapping.IsExtended)
        {
            pressParameter |= ExtendedKeyFlag;
        }

        var messages = new List<KeyMessage>
        {
            new(MessageKeyDown, mapping.VirtualKey, pressParameter),
            new(MessageKeyUp, mapping.VirtualKey, pressParameter | ReleaseFlags),
        };

        return Result<IReadOnlyList<KeyMessage>>.Ok(messages);
    }

    /// <summary>
    /// Presses the named key. Must run on the UI thread. Fails when the platform is not Windows, the press
    /// cannot be planned, the UI thread has no focused window, or the messages cannot be posted.
    /// </summary>
    public static Result PressKey(string key, bool command, bool control, bool shift, bool option)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Result.Fail("Simulated key presses through posted key messages are supported on Windows only.");
        }

        var planResult = PlanKeyMessages(key, command, control, shift, option);
        if (planResult.IsFailure)
        {
            return planResult;
        }

        // Empty while another application holds the keyboard.
        var focusedWindow = GetFocus();
        if (focusedWindow == IntPtr.Zero)
        {
            return Result.Fail("The application has no focused window to receive the key press.");
        }

        foreach (var message in planResult.Value)
        {
            if (!PostMessage(focusedWindow, message.Message, (nint)message.VirtualKey, (nint)message.Parameter))
            {
                return Result.Fail($"Posting the key press failed (error {Marshal.GetLastWin32Error()}).");
            }
        }

        return Result.Ok();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr windowHandle, uint message, nint wParam, nint lParam);
}
