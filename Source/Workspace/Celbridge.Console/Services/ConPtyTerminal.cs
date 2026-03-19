#if WINDOWS

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Celbridge.Console.Services;

public sealed class ConPtyTerminal : IDisposable
{
    private IntPtr _pseudoConsoleHandle = IntPtr.Zero;
    private IntPtr _hInputWrite;
    private IntPtr _hOutputRead;
    private IntPtr _hProcessHandle;
    private IntPtr _hThreadHandle;
    private IntPtr _lpAttributeList;

    private Process? _childProcess;
    private Task? _outputReaderTask;
    private Task? _processMonitorTask;
    private CancellationTokenSource? _cancellationTokenSource;

    private int _cols = -1;
    private int _rows = -1;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler? ProcessExited;

    public void Start(string commandLine, string workingDir)
    {
        // Use the stored values that were reported earlier when xterm.js initialized, via SetSize().
        // Use reasonable defaults if these haven't been set for some reason.
        int columns = _cols > -1 ? _cols : 80;
        int rows = _rows > -1 ? _rows : 25;

        var sa = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true
        };

        CreatePipe(out var hInputRead, out _hInputWrite, ref sa, 0);
        CreatePipe(out _hOutputRead, out var hOutputWrite, ref sa, 0);

        var size = new COORD((short)columns, (short)rows);
        var result = CreatePseudoConsole(size, hInputRead, hOutputWrite, 0, out _pseudoConsoleHandle);
        if (result != 0)
            throw new Win32Exception(result, "CreatePseudoConsole failed");

        CloseHandle(hInputRead);
        CloseHandle(hOutputWrite);

        var siEx = new STARTUPINFOEX();
        siEx.StartupInfo.cb = Marshal.SizeOf(siEx);
        IntPtr lpSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);
        _lpAttributeList = Marshal.AllocHGlobal(lpSize);
        siEx.lpAttributeList = _lpAttributeList;
        InitializeProcThreadAttributeList(siEx.lpAttributeList, 1, 0, ref lpSize);

        UpdateProcThreadAttribute(
            siEx.lpAttributeList,
            0,
            (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
            _pseudoConsoleHandle,
            (IntPtr)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero);

        var pi = new PROCESS_INFORMATION();
        bool success = CreateProcess(
            null,
            new StringBuilder(commandLine),
            IntPtr.Zero,
            IntPtr.Zero,
            true,
            EXTENDED_STARTUPINFO_PRESENT,
            IntPtr.Zero,
            workingDir,
            ref siEx,
            out pi);

        if (!success)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess failed");
        }

        _hProcessHandle = pi.hProcess;
        _hThreadHandle = pi.hThread;

        _childProcess = Process.GetProcessById(pi.dwProcessId);
        _cancellationTokenSource = new CancellationTokenSource();
        _outputReaderTask = Task.Run(() => ReadOutputLoop(_cancellationTokenSource.Token));
        _processMonitorTask = Task.Run(() => MonitorProcessExit(_cancellationTokenSource.Token));
    }

    private async Task ReadOutputLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        try
        {
            using var reader = new FileStream(new SafeFileHandle(_hOutputRead, ownsHandle: false), FileAccess.Read);

            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                OutputReceived?.Invoke(this, text);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested - this is expected during disposal
        }
        catch (IOException)
        {
            // Handle is closed/invalid - this is expected during disposal
        }
        catch (ObjectDisposedException)
        {
            // FileStream was disposed - this is expected during disposal
        }
    }

    private async Task MonitorProcessExit(CancellationToken cancellationToken)
    {
        try
        {
            if (_childProcess == null)
            {
                return;
            }

            // Wait for the process to exit or cancellation
            while (!_childProcess.HasExited && 
                !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken);
            }

            // If the process exited (not cancelled), fire the event
            if (!cancellationToken.IsCancellationRequested &&
                _childProcess.HasExited)
            {
                ProcessExited?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested - this is expected during disposal
        }
        catch (Exception)
        {
            // Ignore exceptions during monitoring
        }
    }

    public void Write(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        WriteFile(_hInputWrite, bytes, bytes.Length, out _, IntPtr.Zero);
    }

    public void SetSize(int cols, int rows)
    {
        if (cols == _cols && rows == _rows)
        {
            return;
        }

        _cols = cols;
        _rows = rows;

        if (_pseudoConsoleHandle == IntPtr.Zero)
        {
            // The psuedo console has not initialized yet.
            // Record the cols & rows so we can apply them when Start() is called.
            return;
        }

        var size = new COORD((short)_cols, (short)_rows);
        int hr = ResizePseudoConsole(_pseudoConsoleHandle, size);
        if (hr != 0)
            throw new Win32Exception(hr, "ResizePseudoConsole failed");
    }

    public void Dispose()
    {
        // Cancel the read loop first to prevent new read operations
        _cancellationTokenSource?.Cancel();

        // Close the input pipe first to signal the process that no more input is coming
        if (_hInputWrite != IntPtr.Zero)
        {
            CloseHandle(_hInputWrite);
            _hInputWrite = IntPtr.Zero;
        }

        // Try to terminate the child process gracefully
        if (_childProcess != null && !_childProcess.HasExited)
        {
            try
            {
                // Give the process a chance to exit gracefully
                if (!_childProcess.WaitForExit(1000))
                {
                    _childProcess.Kill();
                }
            }
            catch
            {
                // Process may have already exited
            }
            finally
            {
                _childProcess?.Dispose();
                _childProcess = null;
            }
        }

        // Wait for the output reader task to complete
        if (_outputReaderTask != null)
        {
            try
            {
                _outputReaderTask.Wait(1000);
            }
            catch
            {
                // Task may have already completed or been cancelled
            }
        }

        // Wait for the process monitor task to complete
        if (_processMonitorTask != null)
        {
            try
            {
                _processMonitorTask.Wait(1000);
            }
            catch
            {
                // Task may have already completed or been cancelled
            }
        }

        // Close the output pipe
        if (_hOutputRead != IntPtr.Zero)
        {
            CloseHandle(_hOutputRead);
            _hOutputRead = IntPtr.Zero;
        }

        // Close the pseudo console
        if (_pseudoConsoleHandle != IntPtr.Zero)
        {
            ClosePseudoConsole(_pseudoConsoleHandle);
            _pseudoConsoleHandle = IntPtr.Zero;
        }

        // Close process and thread handles
        if (_hProcessHandle != IntPtr.Zero)
        {
            CloseHandle(_hProcessHandle);
            _hProcessHandle = IntPtr.Zero;
        }

        if (_hThreadHandle != IntPtr.Zero)
        {
            CloseHandle(_hThreadHandle);
            _hThreadHandle = IntPtr.Zero;
        }

        // Free the attribute list memory
        if (_lpAttributeList != IntPtr.Zero)
        {
            DeleteProcThreadAttributeList(_lpAttributeList);
            Marshal.FreeHGlobal(_lpAttributeList);
            _lpAttributeList = IntPtr.Zero;
        }

        // Dispose the cancellation token source
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    #region Win32 Interop

    private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out IntPtr hReadPipe,
        out IntPtr hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, int nNumberOfBytesToWrite,
        out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        [In] ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;

        public COORD(short x, short y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    #endregion
}

#endif
