using Celbridge.Logging;
using System.Reflection;

namespace Celbridge.Projects.Services;

/// <summary>
/// Discovers and manages migration steps, providing ordered execution based on version numbers.
/// Uses reflection to automatically find all IMigrationStep implementations in the assembly.
/// </summary>
public class MigrationStepRegistry
{
    private readonly ILogger<MigrationStepRegistry> _logger;
    private readonly List<IMigrationStep> _steps = new();
    private bool _initialized = false;

    public MigrationStepRegistry(ILogger<MigrationStepRegistry> logger)
    {
        _logger = logger;
    }

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var stepTypes = assembly.GetTypes()
                .Where(t => typeof(IMigrationStep).IsAssignableFrom(t) && 
                           !t.IsInterface && 
                           !t.IsAbstract)
                .ToList();

            _logger.LogInformation($"Discovered {stepTypes.Count} migration step type(s)");

            foreach (var stepType in stepTypes)
            {
                try
                {
                    var step = Activator.CreateInstance(stepType) as IMigrationStep;
                    if (step != null)
                    {
                        _steps.Add(step);
                        _logger.LogDebug($"Registered migration step: {stepType.Name} (Target: {step.TargetVersion})");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to instantiate migration step: {stepType.Name}");
                }
            }

            // Sort steps by target version in ascending order
            _steps.Sort((a, b) => a.TargetVersion.CompareTo(b.TargetVersion));

            _logger.LogInformation($"Migration steps registered and ordered: {string.Join(", ", _steps.Select(s => s.TargetVersion))}");

            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize migration step registry");
            throw;
        }
    }

    /// <summary>
    /// Get all migration steps that need to be executed to bring a project from
    /// the specified version up to the target version (or latest if no target specified).
    /// </summary>
    public List<IMigrationStep> GetRequiredSteps(Version currentVersion, Version targetVersion)
    {
        if (!_initialized)
        {
            Initialize();
        }

        return _steps
            .Where(s => s.TargetVersion > currentVersion && s.TargetVersion <= targetVersion)
            .ToList();
    }
}
