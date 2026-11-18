using System.Diagnostics;
using Celbridge.Logging;

namespace Celbridge.Python.Services;

/// <summary>
/// A run of uv: the arguments, the variables to set in uv's environment, and how long the run may take.
/// </summary>
public sealed record UvInvocation(
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Variables,
    TimeSpan Timeout);

/// <summary>
/// Describes the result of a run of uv: the exit code and the output. The exit code is null when the run timed out
/// and was stopped.
/// </summary>
public sealed record UvRunOutcome(
    int? ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool TimedOut => ExitCode is null;
}

/// <summary>
/// Runs the uv from the uv dependency. The inherited variables that control uv or Python are left out of uv's
/// environment, so only the invocation's variables configure uv.
/// </summary>
public interface IUvRunner
{
    /// <summary>
    /// Runs uv and waits for uv to exit. A run that outlasts the invocation's timeout is stopped. Fails only when uv
    /// fails to start, and the caller judges the exit code.
    /// </summary>
    Task<Result<UvRunOutcome>> RunAsync(UvInvocation invocation);
}

public sealed class UvRunner : IUvRunner
{
    private readonly UvDependency _uvDependency;
    private readonly ILogger<UvRunner> _logger;

    public UvRunner(
        UvDependency uvDependency,
        ILogger<UvRunner> logger)
    {
        _uvDependency = uvDependency;
        _logger = logger;
    }

    public async Task<Result<UvRunOutcome>> RunAsync(UvInvocation invocation)
    {
        var uvPath = _uvDependency.ExecutablePath;
        var startInfo = new ProcessStartInfo
        {
            FileName = uvPath,
            WorkingDirectory = _uvDependency.FolderPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var removedNames = PythonEnvironmentFilter.Apply(startInfo.Environment);
        if (removedNames.Count > 0)
        {
            _logger.LogInformation(
                "Left the inherited uv and Python variables out of the uv run: {Names}",
                string.Join(", ", removedNames));
        }

        foreach (var (name, value) in invocation.Variables)
        {
            startInfo.Environment[name] = value;
        }

        _logger.LogDebug("Running uv: {FileName} {Arguments}", uvPath, string.Join(' ', invocation.Arguments));

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            return Result<UvRunOutcome>.Fail($"Failed to start uv at '{uvPath}'")
                .WithException(exception);
        }

        if (process is null)
        {
            return Result<UvRunOutcome>.Fail($"Failed to start uv at '{uvPath}'");
        }

        using (process)
        {
            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            int? exitCode;
            using var timeoutCancellation = new CancellationTokenSource(invocation.Timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCancellation.Token);
                exitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                exitCode = null;

                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception exception)
                {
                    // The process can exit on its own first, and leave nothing to stop.
                    _logger.LogDebug(exception, "Failed to stop the timed out uv run");
                }
            }

            // The output is read after a timeout too, because a timed out run's output explains the most. The readers
            // also have to finish before the process is disposed.
            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;

            var outcome = new UvRunOutcome(exitCode, standardOutput, standardError);

            return Result<UvRunOutcome>.Ok(outcome);
        }
    }
}
