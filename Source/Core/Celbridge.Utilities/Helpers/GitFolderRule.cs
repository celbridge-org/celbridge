using Celbridge.Projects;

namespace Celbridge.Utilities;

/// <summary>
/// The rule that reserves the gitignore line '.git', in any letter case. The line matches a .git folder or file at
/// any depth, and everything inside a .git folder. The Windows and macOS file systems ignore letter case, so .GIT
/// names the same folder as .git there.
/// </summary>
public static class GitFolderRule
{
    /// <summary>
    /// The rule's gitignore line.
    /// </summary>
    public static string Pattern => ProjectConstants.GitFolder;

    /// <summary>
    /// The rule's line, compiled to ignore letter case.
    /// </summary>
    public static GitignoreMatcher Matcher { get; } = GitignoreMatcher.Compile([ProjectConstants.GitFolder], ignoreCase: true).Value;
}
