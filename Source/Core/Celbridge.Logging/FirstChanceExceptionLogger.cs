using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace Celbridge.Logging;

/// <summary>
/// Logs every first-chance exception (including caught ones) with its type,
/// message, and originating user-code frame. DEBUG-only diagnostic; install
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
            // File name only; the type name already locates the file.
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
