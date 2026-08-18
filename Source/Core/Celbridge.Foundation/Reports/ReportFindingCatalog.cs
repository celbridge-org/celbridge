namespace Celbridge.Reports;

/// <summary>
/// The catalog of every finding the host can report, declared once and grouped by area. The source
/// of truth for what finding codes exist. A code is never reused or renumbered: reports persist on
/// disk, so a recycled code silently changes what an old report said. Each message template is a
/// localization key resolved against the host's resources when the finding is built.
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
            new(new ReportCode("CEL_PROJECT_001"), "Report_Finding_Project_MigrationNotReached", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor UpgradeCancelled =
            new(new ReportCode("CEL_PROJECT_002"), "Report_Finding_Project_UpgradeCancelled", ReportSeverity.Warning);

        public static readonly ReportFindingDescriptor MigrationFailed =
            new(new ReportCode("CEL_PROJECT_003"), "Report_Finding_Project_MigrationFailed", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor LoadFailed =
            new(new ReportCode("CEL_PROJECT_004"), "Report_Finding_Project_LoadFailed", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor ConfigEntrySkipped =
            new(new ReportCode("CEL_PROJECT_005"), "Report_Finding_Project_ConfigEntrySkipped", ReportSeverity.Warning);
    }

    /// <summary>
    /// Discovering packages and resolving the editors they contribute.
    /// </summary>
    public static class Package
    {
        public static readonly ReportFindingDescriptor PackageLoadFailed =
            new(new ReportCode("CEL_PACKAGE_001"), "Report_Finding_Package_PackageLoadFailed", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor EditorSkipped =
            new(new ReportCode("CEL_PACKAGE_002"), "Report_Finding_Package_EditorSkipped", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor EditorDegraded =
            new(new ReportCode("CEL_PACKAGE_003"), "Report_Finding_Package_EditorDegraded", ReportSeverity.Warning);
    }

    /// <summary>
    /// The project's resources: the state of its sidecar files and the references between them, and
    /// what an operation over them could not do.
    /// </summary>
    public static class Resource
    {
        public static readonly ReportFindingDescriptor OrphanSidecar =
            new(new ReportCode("CEL_RESOURCE_001"), "Report_Finding_Resource_OrphanSidecar", ReportSeverity.Warning);

        public static readonly ReportFindingDescriptor BrokenSidecar =
            new(new ReportCode("CEL_RESOURCE_002"), "Report_Finding_Resource_BrokenSidecar", ReportSeverity.Warning);

        public static readonly ReportFindingDescriptor MissingReference =
            new(new ReportCode("CEL_RESOURCE_003"), "Report_Finding_Resource_MissingReference", ReportSeverity.Warning);

        public static readonly ReportFindingDescriptor CopyFailed =
            new(new ReportCode("CEL_RESOURCE_004"), "Report_Finding_Resource_CopyFailed", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor MoveFailed =
            new(new ReportCode("CEL_RESOURCE_005"), "Report_Finding_Resource_MoveFailed", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor DeleteFailed =
            new(new ReportCode("CEL_RESOURCE_006"), "Report_Finding_Resource_DeleteFailed", ReportSeverity.Error);

        public static readonly ReportFindingDescriptor ReferenceNotUpdated =
            new(new ReportCode("CEL_RESOURCE_007"), "Report_Finding_Resource_ReferenceNotUpdated", ReportSeverity.Warning);
    }
}
