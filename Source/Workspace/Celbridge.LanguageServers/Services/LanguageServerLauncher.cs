using System.Diagnostics;
using Celbridge.Logging;
using Celbridge.Utilities.Platform;

namespace Celbridge.LanguageServers.Services;

/// <summary>
/// A running language server process.
/// </summary>
internal interface ILanguageServerProcess : IDisposable
{
    /// <summary>
    /// Returns the stream that writes to the server's standard input.
    /// </summary>
    Stream Input { get; }

    /// <summary>
    /// Returns the stream that reads the server's standard output.
    /// </summary>
    Stream Output { get; }

    /// <summary>
    /// Completes when the server's process has exited.
    /// </summary>
    Task Exited { get; }

    /// <summary>
    /// Ends the server's process at once.
    /// </summary>
    void Kill();
}

/// <summary>
/// Starts language server processes.
/// </summary>
internal interface ILanguageServerLauncher
{
    /// <summary>
    /// Starts a server with the folder as its working folder.
    /// </summary>
    Result<ILanguageServerProcess> Start(LanguageServerInvocation invocation, string workingFolderPath);
}

/// <summary>
/// Starts each language server as a child process that speaks over stdio.
/// </summary>
internal sealed class LanguageServerLauncher : ILanguageServerLauncher, IDisposable
{
    private readonly ILogger<LanguageServerLauncher> _logger;
    private readonly WindowsJobObject? _jobObject;

    public LanguageServerLauncher(ILogger<LanguageServerLauncher> logger)
    {
        _logger = logger;

        // The servers join a job that ends them when Celbridge's process ends.
        if (OperatingSystem.IsWindows())
        {
            _jobObject = new WindowsJobObject();
        }
    }

    public Result<ILanguageServerProcess> Start(LanguageServerInvocation invocation, string workingFolderPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.ExecutablePath,
            WorkingDirectory = workingFolderPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var name in invocation.RemovedEnvironmentVariables)
        {
            startInfo.Environment.Remove(name);
        }

        foreach (var (name, value) in invocation.EnvironmentVariables)
        {
            startInfo.Environment[name] = value;
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            return Result<ILanguageServerProcess>.Fail($"Failed to start '{invocation.ExecutablePath}'")
                .WithException(exception);
        }

        if (process is null)
        {
            return Result<ILanguageServerProcess>.Fail($"Failed to start '{invocation.ExecutablePath}'");
        }

        _jobObject?.AssignProcess(process.Id);

        var serverProcess = new LanguageServerProcess(process, _logger);

        return Result<ILanguageServerProcess>.Ok(serverProcess);
    }

    public void Dispose()
    {
        _jobObject?.Dispose();
    }

    private sealed class LanguageServerProcess : ILanguageServerProcess
    {
        private readonly Process _process;
        private readonly ILogger _logger;

        public LanguageServerProcess(Process process, ILogger logger)
        {
            _process = process;
            _logger = logger;

            Input = process.StandardInput.BaseStream;
            Output = process.StandardOutput.BaseStream;
            Exited = process.WaitForExitAsync();

            // A server writes its log to standard error. A full pipe would block the server, so the pipe is read.
            // The id is read now, because a disposed process has no id.
            var processId = process.Id;
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    _logger.LogDebug("Language server {ProcessId}: {Line}", processId, args.Data);
                }
            };
            process.BeginErrorReadLine();
        }

        public Stream Input { get; }

        public Stream Output { get; }

        public Task Exited { get; }

        public void Kill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to kill the language server process");
            }
        }

        public void Dispose()
        {
            _process.Dispose();
        }
    }
}
