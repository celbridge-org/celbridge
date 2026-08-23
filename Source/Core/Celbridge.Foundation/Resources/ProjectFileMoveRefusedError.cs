using Celbridge.Projects;

namespace Celbridge.Resources;

/// <summary>
/// Why a move of the project file was refused.
/// </summary>
public enum ProjectFileMoveRefusal
{
    /// <summary>
    /// The destination is not the project root, so the move would orphan the project rather than
    /// relocate it.
    /// </summary>
    OutsideProjectFolder,

    /// <summary>
    /// The destination drops the project file extension, which is how a project is found to be opened.
    /// </summary>
    ExtensionChanged,
}

/// <summary>
/// Attached to the failed Result when a move of the project file is refused, so the surface presenting
/// the failure writes the reason in the user's language rather than echoing the service's diagnostic.
/// </summary>
public sealed class ProjectFileMoveRefusedError : Exception
{
    /// <summary>
    /// Why the move was refused.
    /// </summary>
    public ProjectFileMoveRefusal Refusal { get; }

    public ProjectFileMoveRefusedError(ProjectFileMoveRefusal refusal)
        : base(FormatMessage(refusal))
    {
        Refusal = refusal;
    }

    private static string FormatMessage(ProjectFileMoveRefusal refusal)
    {
        switch (refusal)
        {
            case ProjectFileMoveRefusal.OutsideProjectFolder:
                return "The project folder is the folder the project file sits in, so the project file cannot be moved out of it.";

            default:
                return $"The project file must keep its {ProjectConstants.ProjectFileExtension} extension.";
        }
    }
}
