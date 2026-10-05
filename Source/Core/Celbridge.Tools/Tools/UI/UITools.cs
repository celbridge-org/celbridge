using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Test automation tools that find and act on the application's own controls, press keys and answer dialogs.
/// </summary>
[McpServerToolType]
public partial class UITools : AgentToolBase
{
    // The error a control lookup returns when every field of its query is empty.
    private const string EmptyQueryMessage = "At least one of automationId, name or controlType is required.";

    public UITools(IApplicationServiceProvider services) : base(services) { }
}
