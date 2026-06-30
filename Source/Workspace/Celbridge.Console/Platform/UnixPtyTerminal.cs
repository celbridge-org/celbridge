using System.Runtime.InteropServices;
using System.Text;
using Celbridge.Console.Services;

namespace Celbridge.Console.Platform;

/// <summary>
/// A pseudo-terminal backend for the macOS and Linux heads. It allocates a pty with openpty, spawns
/// the command line through /bin/sh under a new session with posix_spawn (the slave becomes the child's
/// stdin/stdout/stderr), streams the master output, and resizes via the TIOCSWINSZ ioctl. forkpty is
/// avoided deliberately: fork in a managed, multi-threaded process is unsafe, whereas posix_spawn is.
/// </summary>
internal sealed class UnixPtyTerminal : IPtyBackend
{
    private const string LibSystem = "/usr/lib/libSystem.dylib";

    // TIOCSWINSZ on Darwin: _IOW('t', 103, struct winsize), an 8-byte winsize.
    private const ulong TIOCSWINSZ = 0x80087467;

    // posix_spawn: start the child in its own session so it owns the pty (macOS 10.15+).
    private const short POSIX_SPAWN_SETSID = 0x0400;

    private const int SIGHUP = 1;
    private const int SIGKILL = 9;

    // ioctl is variadic. Apple's ARM64 ABI passes all variadic arguments on the stack, whereas every
    // other target (x86_64, and ARM64 on Linux) passes them in registers like fixed arguments. So only
    // on Apple Silicon does the winsize pointer need the stack-spilling shim below; elsewhere the plain
    // ref overload is correct.
    private static readonly bool UseDarwinArm64IoctlShim =
        OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    private readonly object _processLock = new();
    private int _masterFd = -1;
    private int _childPid = -1;
    private bool _childExited;

    private Thread? _outputReaderThread;
    private Thread? _processMonitorThread;
    private CancellationTokenSource? _cancellationTokenSource;

    private int _cols = -1;
    private int _rows = -1;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler? ProcessExited;

    public void Start(string commandLine, string workingDir, Dictionary<string, string>? environmentVariables = null)
    {
        // Use the size xterm.js reported earlier via SetSize(), or reasonable defaults if it has not yet.
        int columns = _cols > -1 ? _cols : 80;
        int rows = _rows > -1 ? _rows : 25;

        var initialSize = new Winsize
        {
            ws_row = (ushort)rows,
            ws_col = (ushort)columns
        };

        int openResult = openpty(out _masterFd, out int slaveFd, IntPtr.Zero, IntPtr.Zero, ref initialSize);
        if (openResult != 0)
        {
            throw new InvalidOperationException($"openpty failed (errno {Marshal.GetLastWin32Error()})");
        }

        // Run the command line through a shell, the Unix equivalent of how ConPty's CreateProcess parses
        // a command-line string. A trailing null terminates the argv/envp pointer arrays for posix_spawn.
        var argv = new string?[] { "/bin/sh", "-c", commandLine, null };
        var envp = BuildEnvironmentArray(environmentVariables);

        IntPtr fileActions = IntPtr.Zero;
        IntPtr spawnAttributes = IntPtr.Zero;
        try
        {
            posix_spawn_file_actions_init(out fileActions);
            posix_spawn_file_actions_adddup2(ref fileActions, slaveFd, 0);
            posix_spawn_file_actions_adddup2(ref fileActions, slaveFd, 1);
            posix_spawn_file_actions_adddup2(ref fileActions, slaveFd, 2);
            posix_spawn_file_actions_addclose(ref fileActions, slaveFd);
            posix_spawn_file_actions_addclose(ref fileActions, _masterFd);

            if (!string.IsNullOrEmpty(workingDir))
            {
                posix_spawn_file_actions_addchdir_np(ref fileActions, workingDir);
            }

            posix_spawnattr_init(out spawnAttributes);
            posix_spawnattr_setflags(ref spawnAttributes, POSIX_SPAWN_SETSID);

            int spawnResult = posix_spawn(out _childPid, "/bin/sh", ref fileActions, ref spawnAttributes, argv, envp);
            if (spawnResult != 0)
            {
                throw new InvalidOperationException($"posix_spawn failed (error {spawnResult})");
            }
        }
        finally
        {
            if (fileActions != IntPtr.Zero)
            {
                posix_spawn_file_actions_destroy(ref fileActions);
            }
            if (spawnAttributes != IntPtr.Zero)
            {
                posix_spawnattr_destroy(ref spawnAttributes);
            }

            // The child holds its own dup'd copies of the slave; the parent does not need it.
            close(slaveFd);
        }

        _cancellationTokenSource = new CancellationTokenSource();

        // Both loops make blocking syscalls (read, waitpid) for the process lifetime, so they run on
        // dedicated background threads rather than starving the thread pool.
        _outputReaderThread = new Thread(() => ReadOutputLoop(_cancellationTokenSource.Token))
        {
            IsBackground = true,
            Name = "UnixPtyTerminal.Output"
        };
        _outputReaderThread.Start();

        _processMonitorThread = new Thread(() => MonitorProcessExit(_cancellationTokenSource.Token))
        {
            IsBackground = true,
            Name = "UnixPtyTerminal.Monitor"
        };
        _processMonitorThread.Start();
    }

    private void ReadOutputLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                nint bytesRead = read(_masterFd, buffer, (nint)buffer.Length);

                // 0 = the slave closed (child exited); -1 = read error (EIO on macOS once the child is
                // gone, or EBADF when the master is closed during disposal). Either way the stream ends.
                if (bytesRead <= 0)
                {
                    break;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, (int)bytesRead);
                OutputReceived?.Invoke(this, text);
            }
        }
        catch (Exception)
        {
            // Expected during disposal once the master descriptor is closed.
        }
    }

    private void MonitorProcessExit(CancellationToken cancellationToken)
    {
        try
        {
            // Blocks until the child exits, reaping it (so it does not linger as a zombie). On disposal
            // the child is killed, which unblocks this; the cancellation check then suppresses the event.
            waitpid(_childPid, out _, 0);

            lock (_processLock)
            {
                _childExited = true;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                ProcessExited?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception)
        {
            // Ignore exceptions during monitoring.
        }
    }

    public void Write(string input)
    {
        if (_masterFd < 0)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(input);
        var remaining = bytes.AsSpan();
        while (remaining.Length > 0)
        {
            var chunk = remaining.ToArray();
            nint written = write(_masterFd, chunk, (nint)chunk.Length);
            if (written <= 0)
            {
                break;
            }

            remaining = remaining[(int)written..];
        }
    }

    public void SetSize(int cols, int rows)
    {
        if (cols == _cols && rows == _rows)
        {
            return;
        }

        _cols = cols;
        _rows = rows;

        if (_masterFd < 0)
        {
            // The pty has not been allocated yet; the size is applied when Start() opens it.
            return;
        }

        var size = new Winsize
        {
            ws_row = (ushort)rows,
            ws_col = (ushort)cols
        };

        // Setting the window size also delivers SIGWINCH to the foreground process group.
        if (UseDarwinArm64IoctlShim)
        {
            var sizeHandle = GCHandle.Alloc(size, GCHandleType.Pinned);
            try
            {
                // The six IntPtr.Zero arguments fill registers x2..x7 so the winsize pointer spills to
                // the stack, where Apple's variadic ioctl reads its first variadic argument from.
                ioctl(
                    _masterFd,
                    TIOCSWINSZ,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    sizeHandle.AddrOfPinnedObject());
            }
            finally
            {
                sizeHandle.Free();
            }
        }
        else
        {
            ioctl(_masterFd, TIOCSWINSZ, ref size);
        }
    }

    /// <summary>
    /// Builds the null-terminated environment array for posix_spawn: the current process environment
    /// merged with the provided variables (which take precedence), plus a default TERM so the child
    /// behaves as an interactive terminal.
    /// </summary>
    private static string?[] BuildEnvironmentArray(Dictionary<string, string>? environmentVariables)
    {
        var mergedEnvironment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key
                && entry.Value is string value)
            {
                mergedEnvironment[key] = value;
            }
        }

        if (environmentVariables is not null)
        {
            foreach (var entry in environmentVariables)
            {
                mergedEnvironment[entry.Key] = entry.Value;
            }
        }

        if (!mergedEnvironment.ContainsKey("TERM"))
        {
            mergedEnvironment["TERM"] = "xterm-256color";
        }

        var entries = mergedEnvironment
            .Select(pair => $"{pair.Key}={pair.Value}")
            .Cast<string?>()
            .ToList();

        // posix_spawn reads envp until a null pointer.
        entries.Add(null);

        return entries.ToArray();
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();

        // Terminate the child first. Closing the master alone does not interrupt the reader's pending
        // read on macOS; killing the child closes the slave, so the read returns and the loop ends. The
        // lock pairs the kill with the monitor's reap so an already-reaped (and possibly reused) pid is
        // never signalled.
        lock (_processLock)
        {
            if (_childPid > 0
                && !_childExited)
            {
                kill(_childPid, SIGHUP);
                kill(_childPid, SIGKILL);
            }
        }

        _outputReaderThread?.Join(2000);
        _processMonitorThread?.Join(2000);

        if (_masterFd != -1)
        {
            close(_masterFd);
            _masterFd = -1;
        }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Winsize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int openpty(out int amaster, out int aslave, IntPtr name, IntPtr termp, ref Winsize winp);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int posix_spawn(
        out int pid,
        string path,
        ref IntPtr fileActions,
        ref IntPtr attrp,
        string?[] argv,
        string?[] envp);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int posix_spawn_file_actions_init(out IntPtr fileActions);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int posix_spawn_file_actions_destroy(ref IntPtr fileActions);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int posix_spawn_file_actions_adddup2(ref IntPtr fileActions, int filedes, int newfiledes);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int posix_spawn_file_actions_addclose(ref IntPtr fileActions, int filedes);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int posix_spawn_file_actions_addchdir_np(ref IntPtr fileActions, string path);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int posix_spawnattr_init(out IntPtr attr);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int posix_spawnattr_destroy(ref IntPtr attr);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int posix_spawnattr_setflags(ref IntPtr attr, short flags);

    [DllImport(LibSystem, EntryPoint = "ioctl", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref Winsize windowSize);

    // Apple ARM64 variadic shim: the six padding arguments occupy the argument registers so the trailing
    // pointer is passed on the stack, the slot a variadic callee reads. Used only on Apple Silicon.
    [DllImport(LibSystem, EntryPoint = "ioctl", SetLastError = true)]
    private static extern int ioctl(
        int fd,
        ulong request,
        IntPtr pad2,
        IntPtr pad3,
        IntPtr pad4,
        IntPtr pad5,
        IntPtr pad6,
        IntPtr pad7,
        IntPtr windowSize);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern nint read(int fd, byte[] buffer, nint count);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern nint write(int fd, byte[] buffer, nint count);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int close(int fd);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int kill(int pid, int sig);
}
