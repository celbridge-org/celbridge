using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;

namespace Celbridge.Logging;

/// <summary>
/// Logs every first-chance exception (including caught ones) with its type,
/// message, and originating user-code frame. DEBUG-only diagnostic. Install
/// once at app startup.
/// </summary>
public static class FirstChanceExceptionLogger
{
    // Frame namespaces treated as framework noise when locating the user throw site.
    private static readonly string[] SkippedNamespacePrefixes = new[]
    {
        "System.",
        "Microsoft.",
        "Uno.",
        "Windows.",
    };

    // Exception types whose throw is expected control flow, not a bug. Matched
    // by full type name so this assembly stays free of the StreamJsonRpc reference.
    private static readonly HashSet<string> SuppressedExceptionTypeFullNames = new(StringComparer.Ordinal)
    {
        "StreamJsonRpc.RemoteMethodNotFoundException",
    };

    // Socket errors that mean the peer went away rather than that anything failed. Every client that
    // holds a loopback connection produces one of these when it closes: a console document, a WebView
    // navigating away, a Python session exiting. The read that was waiting on the connection throws once
    // per await boundary as it unwinds, so a single ordinary close logs a burst of them.
    private static readonly HashSet<SocketError> DisconnectSocketErrors = new()
    {
        SocketError.ConnectionReset,
        SocketError.ConnectionAborted,
        SocketError.Shutdown,
        SocketError.OperationAborted,
        SocketError.Interrupted,
    };

    private static int _installed;

    // Recursion guard: the logger itself can throw and re-enter this handler.
    private static readonly ThreadLocal<bool> _isLogging = new(() => false);

    /// <summary>
    /// Wires the AppDomain.FirstChanceException handler. Idempotent.
    /// </summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (_isLogging.Value)
        {
            return;
        }

        _isLogging.Value = true;
        try
        {
            if (ServiceLocator.ServiceProvider is null)
            {
                return;
            }

            var exception = args.Exception;

            // Cancellation is expected control flow during teardown and shutdown, not a bug worth logging.
            // OperationCanceledException also covers its subclass TaskCanceledException.
            if (exception is OperationCanceledException)
            {
                return;
            }

            if (SuppressedExceptionTypeFullNames.Contains(exception.GetType().FullName ?? string.Empty))
            {
                return;
            }

            if (IsExpectedDisconnect(exception))
            {
                return;
            }

            var originatingFrame = FindOriginatingFrame();
            var location = FormatLocation(originatingFrame);

            var logger = ServiceLocator.AcquireService<ILogger<FirstChanceExceptionLoggerCategory>>();
            logger.LogDebug($"FirstChance: {exception.GetType().Name}: {exception.Message} at {location}");
        }
        catch
        {
            // Diagnostics must not break the host.
        }
        finally
        {
            _isLogging.Value = false;
        }
    }

    // A socket stream reports a dropped connection as an IOException wrapping the SocketException that
    // carries the reason, so the reason is what is matched rather than the exception type. Any other
    // IOException still logs, since a real IO failure is worth seeing.
    private static bool IsExpectedDisconnect(Exception exception)
    {
        Exception? candidate = exception;
        if (exception is IOException)
        {
            candidate = exception.InnerException;
        }

        if (candidate is not SocketException socketException)
        {
            return false;
        }

        return DisconnectSocketErrors.Contains(socketException.SocketErrorCode);
    }

    // First stack frame that isn't framework or this logger. Async state-machine
    // frames pass through — their declaring type is the user's nested type.
    private static StackFrame? FindOriginatingFrame()
    {
        var trace = new StackTrace(fNeedFileInfo: true);
        var frames = trace.GetFrames();

        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            if (method is null)
            {
                continue;
            }

            var declaringType = method.DeclaringType;
            if (declaringType is null)
            {
                continue;
            }

            if (declaringType == typeof(FirstChanceExceptionLogger))
            {
                continue;
            }

            var fullName = declaringType.FullName ?? string.Empty;
            if (IsSkippedNamespace(fullName))
            {
                continue;
            }

            return frame;
        }

        return null;
    }

    private static bool IsSkippedNamespace(string fullName)
    {
        foreach (var prefix in SkippedNamespacePrefixes)
        {
            if (fullName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string FormatLocation(StackFrame? frame)
    {
        if (frame is null)
        {
            return "<unknown>";
        }

        var method = frame.GetMethod();
        var typeName = method?.DeclaringType?.FullName ?? "<unknown type>";
        var methodName = method?.Name ?? "<unknown method>";

        var fileName = frame.GetFileName();
        var lineNumber = frame.GetFileLineNumber();
        if (!string.IsNullOrEmpty(fileName)
            && lineNumber > 0)
        {
            // File name only. The type name already locates the file.
            var shortFile = System.IO.Path.GetFileName(fileName);
            return $"{typeName}.{methodName} ({shortFile}:{lineNumber})";
        }

        return $"{typeName}.{methodName}";
    }
}

// Marker type whose FullName becomes the ILogger<T> category for the log line.
internal sealed class FirstChanceExceptionLoggerCategory
{
}
