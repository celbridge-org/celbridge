namespace Celbridge.Reports;

/// <summary>
/// The catalog of every finding the host can report, declared once and grouped by area. The source
/// of truth for what finding codes exist. A code is never reused or renumbered: reports persist on
/// disk, so a recycled code silently changes what an old report said.
/// </summary>
public static class ReportFindingCatalog
{
    /// <summary>
    /// The prefix every host code carries. A contribution's codes are namespaced by its package, so
    /// nothing outside the host can occupy this prefix.
    /// </summary>
    public const string CodePrefix = "CEL_";

    /// <summary>
    /// Loading a project: migration, the config file, and the load itself.
    /// </summary>
    public static class Project
    {
        public static readonly ReportFindingDescriptor MigrationNotReached =
            new(new ReportCode("CEL_PROJECT_001"), "Migration step was not reached.", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor UpgradeCancelled =
            new(new ReportCode("CEL_PROJECT_002"), "The upgrade was cancelled, so the project was not loaded.", ReportSeverity.Warning);

        public static readonly ReportFindingDescriptor MigrationFailed =
            new(new ReportCode("CEL_PROJECT_003"), "Migration failed.", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor LoadFailed =
            new(new ReportCode("CEL_PROJECT_004"), "Project load failed.", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor ConfigEntrySkipped =
            new(new ReportCode("CEL_PROJECT_005"), "Config entry skipped: {0}", ReportSeverity.Warning);
    }

    /// <summary>
    /// Discovering packages and resolving the editors they contribute.
    /// </summary>
    public static class Package
    {
        public static readonly ReportFindingDescriptor PackageLoadFailed =
            new(new ReportCode("CEL_PACKAGE_001"), "Package failed to load: {0}", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor EditorSkipped =
            new(new ReportCode("CEL_PACKAGE_002"), "Editor skipped: {0}", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor EditorDegraded =
            new(new ReportCode("CEL_PACKAGE_003"), "Editor degraded: {0}", ReportSeverity.Warning);
    }

    /// <summary>
    /// The state of the project's resources: sidecar files and the references between them.
    /// </summary>
    public static class Resource
    {
        public static readonly ReportFindingDescriptor OrphanSidecar =
            new(new ReportCode("CEL_RESOURCE_001"), "Orphan .cel file: no resource it describes.", ReportSeverity.Warning);

        public static readonly ReportFindingDescriptor BrokenSidecar =
            new(new ReportCode("CEL_RESOURCE_002"), "Broken .cel file: could not be parsed.", ReportSeverity.Warning);

        public static readonly ReportFindingDescriptor MissingReference =
            new(new ReportCode("CEL_RESOURCE_003"), "References a missing resource.", ReportSeverity.Warning);
    }
}
