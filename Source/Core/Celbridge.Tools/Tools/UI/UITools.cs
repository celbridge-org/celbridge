using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Test automation tools that find and act on the application's own controls, press keys and answer dialogs.
/// </summary>
[McpServerToolType]
public partial class UITools : AgentToolBase
{
    // The error a control lookup gives when its query names nothing to match.
    private const string EmptyQueryMessage =
        "Name at least one of the automation ID, the name and the control type to match.";

    public UITools(IApplicationServiceProvider services) : base(services) { }
}
