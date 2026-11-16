using Celbridge.Commands;

namespace Celbridge.Projects;

/// <summary>
/// Records that the user trusts the currently loaded project, then reloads the project, so the project opens fully.
/// </summary>
public interface ITrustProjectCommand : IExecutableCommand
{}
