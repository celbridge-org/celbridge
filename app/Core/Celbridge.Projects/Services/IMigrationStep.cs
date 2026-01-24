namespace Celbridge.Projects.Services;

/// <summary>
/// Represents a single migration step that upgrades a project from one version to another.
/// Each migration step is responsible for applying all changes needed to bring a project
/// from the previous version up to the target version specified in the step.
/// </summary>
public interface IMigrationStep
{
    /// <summary>
    /// The target version this migration step upgrades to (e.g., "0.1.5").
    /// </summary>
    Version TargetVersion { get; }

    /// <summary>
    /// Apply the migration changes to upgrade the project to the target version.
    /// Users are expected to backup projects before migration.
    /// </summary>
    Task<Result> ApplyAsync(MigrationContext context);
}
