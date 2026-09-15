namespace Celbridge.Workshop;

/// <summary>
/// Well-known names and limits shared by the workshop tools.
/// </summary>
public static class WorkshopConstants
{
    /// <summary>
    /// File name of the generated workshop history written beside a package's manifest.
    /// </summary>
    public const string HistoryFileName = "HISTORY.md";

    /// <summary>
    /// Reserved name that selects a package's highest live workshop version. The workshop refuses it as an alias name.
    /// </summary>
    public const string LatestAlias = "latest";

    /// <summary>
    /// Maximum length of the change summary accompanying a published workshop version.
    /// </summary>
    public const int MaxSummaryLength = 512;
}
