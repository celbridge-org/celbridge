using Celbridge.Commands;

namespace Celbridge.Projects;

/// <summary>
/// How a project load started. It decides what happens when another instance of the application has the project
/// open.
/// </summary>
public enum ProjectLoadOrigin
{
    /// <summary>
    /// An open from a running window, such as Home, the recent projects or File > Open. A project file that the
    /// operating system sends to a running instance opens this way too.
    /// </summary>
    RunningWindow,

    /// <summary>
    /// The instance was started to open this project, from the operating system or the command line.
    /// </summary>
    LaunchFile,

    /// <summary>
    /// A launch opening the project that was open when the application last ran.
    /// </summary>
    PreviousProject
}

/// <summary>
/// Load the project file at the specified path.
/// </summary>
public interface ILoadProjectCommand : IExecutableCommand
{
    string ProjectFilePath { get; set; }

    /// <summary>
    /// How the load started, which decides what happens when another instance has the project open. Defaults to
    /// an open from a running window.
    /// </summary>
    ProjectLoadOrigin Origin { get; set; }
}
