using System.Diagnostics;
using Celbridge.Logging;

namespace Celbridge.Python.Services;

/// <summary>
/// Runs a uv tool install, online first and then offline.
/// </summary>
public sealed class UvToolInstaller
{
    private const string OfflineArgument = "--offline";

    private readonly IUvRunner _uvRunner;
    private readonly ILogger _logger;

    public UvToolInstaller(IUvRunner uvRunner, ILogger logger)
    {
        _uvRunner = uvRunner;
        _logger = logger;
    }

    /// <summary>
    /// Runs the install with the arguments and uv's variables. When the run fails, runs the install again offline.
    /// Fails when both runs fail.
    /// </summary>
    public async Task<Result> InstallAsync(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> variables,
        TimeSpan timeout)
    {
        var onlineInstall = new UvInvocation(arguments, variables, timeout);
        var onlineResult = await RunAsync(onlineInstall);
        if (onlineResult.IsSuccess)
        {
            return Result.Ok();
        }

        // Online, uv asks the package index what to install. When the network is down, uv gives up after three
        // retries. Offline, uv installs from its cache, which keeps the packages and the interpreter of an earlier
        // install.
        _logger.LogInformation("Running the uv tool install again offline");

        var offlineInstall = onlineInstall with { Arguments = [.. arguments, OfflineArgument] };
        var offlineResult = await RunAsync(offlineInstall);
        if (offlineResult.IsFailure)
        {
            return Result.Fail("The uv tool install failed online and offline")
                .WithErrors(onlineResult)
                .WithErrors(offlineResult);
        }

        return Result.Ok();
    }

    private async Task<Result> RunAsync(UvInvocation invocation)
    {
        var runTimer = Stopwatch.StartNew();

        var runResult = await _uvRunner.RunAsync(invocation);
        if (runResult.IsFailure)
        {
            return Result.Fail("Failed to run the uv tool install")
                .WithErrors(runResult);
        }
        var outcome = runResult.Value;
        var standardError = outcome.StandardError.Trim();

        if (outcome.TimedOut)
        {
            _logger.LogWarning("The uv tool install timed out after {Timeout} minutes. Stderr: {Stderr}. Stdout: {Stdout}",
                invocation.Timeout.TotalMinutes, outcome.StandardError, outcome.StandardOutput);

            return Result.Fail($"The uv tool install timed out after {invocation.Timeout.TotalMinutes} minutes. {standardError}");
        }

        if (outcome.ExitCode != 0)
        {
            _logger.LogWarning("The uv tool install exited with code {ExitCode} after {DurationMs}ms. Stderr: {Stderr}. Stdout: {Stdout}",
                outcome.ExitCode, runTimer.ElapsedMilliseconds, outcome.StandardError, outcome.StandardOutput);

            return Result.Fail($"The uv tool install exited with code {outcome.ExitCode}. {standardError}");
        }

        _logger.LogInformation("The uv tool install succeeded in {DurationMs}ms", runTimer.ElapsedMilliseconds);

        return Result.Ok();
    }
}
