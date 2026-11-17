using System.Diagnostics;
using Celbridge.Automation;
using Celbridge.Dialog;
using Celbridge.Logging;
using Celbridge.Settings;

namespace Celbridge.Dependencies.Services;

/// <summary>
/// Installs the dependencies of the registered features. A dependency installs only when the dependency's check
/// finds the pinned version missing. The progress dialog shows the dependency's title during the install.
/// </summary>
internal sealed class DependencyService : IDependencyService
{
    private readonly IReadOnlyList<IFeatureDependencies> _featureDependencies;
    private readonly IFeatureFlags _featureFlags;
    private readonly IDialogService _dialogService;
    private readonly IAutomationService _automationService;
    private readonly ILogger<DependencyService> _logger;

    public DependencyService(
        IEnumerable<IFeatureDependencies> featureDependencies,
        IFeatureFlags featureFlags,
        IDialogService dialogService,
        IAutomationService automationService,
        ILogger<DependencyService> logger)
    {
        _featureDependencies = featureDependencies.ToList();
        _featureFlags = featureFlags;
        _dialogService = dialogService;
        _automationService = automationService;
        _logger = logger;
    }

    public async Task<DependencyInstallFailure?> InstallDependenciesAsync(IReadOnlyDictionary<string, bool> projectFeatures)
    {
        // A dependency that two features share is checked once, for the first feature that lists it.
        var checkedDependencies = new HashSet<IDependency>();

        foreach (var feature in _featureDependencies)
        {
            // The workspace applies the project's values to IFeatureFlags later, so the project's values are passed in.
            if (!_featureFlags.IsEnabledForProject(feature.Feature, projectFeatures))
            {
                continue;
            }

            foreach (var dependency in feature.Dependencies)
            {
                if (!checkedDependencies.Add(dependency))
                {
                    continue;
                }

                var installResult = await EnsureInstalledAsync(dependency);
                if (installResult.IsFailure)
                {
                    return new DependencyInstallFailure(feature.Feature, dependency.Name, installResult);
                }
            }
        }

        return null;
    }

    private async Task<Result> EnsureInstalledAsync(IDependency dependency)
    {
        if (await IsInstalledAsync(dependency))
        {
            _logger.LogDebug("The dependency '{Id}' {Version} is installed", dependency.Id, dependency.Version);
            return Result.Ok();
        }

        _logger.LogInformation("Installing the dependency '{Id}' {Version}", dependency.Id, dependency.Version);
        _automationService.LogEvent(new DependencyInstallingEvent(dependency.Id));

        using var progressToken = _dialogService.AcquireProgressDialog(dependency.ProgressTitle);
        var installTimer = Stopwatch.StartNew();

        Result installResult;
        try
        {
            installResult = await dependency.InstallAsync();
        }
        catch (Exception exception)
        {
            installResult = Result.Fail($"Failed to install the dependency '{dependency.Id}'")
                .WithException(exception);
        }

        if (installResult.IsFailure)
        {
            _logger.LogError(installResult, $"Failed to install the dependency '{dependency.Id}' {dependency.Version}");
            _automationService.LogEvent(new DependencyInstallFailedEvent(dependency.Id));
            return installResult;
        }

        _logger.LogInformation("Installed the dependency '{Id}' {Version} in {DurationMs}ms",
            dependency.Id,
            dependency.Version,
            installTimer.ElapsedMilliseconds);
        _automationService.LogEvent(new DependencyInstalledEvent(dependency.Id, installTimer.ElapsedMilliseconds));

        return Result.Ok();
    }

    // A check that throws counts as a missing dependency, so the install runs and reports what is wrong.
    private async Task<bool> IsInstalledAsync(IDependency dependency)
    {
        try
        {
            return await dependency.IsInstalledAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, $"The check for the dependency '{dependency.Id}' failed");
            return false;
        }
    }
}
