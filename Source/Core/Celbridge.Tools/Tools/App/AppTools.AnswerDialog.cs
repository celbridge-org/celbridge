#if DEBUG
using Celbridge.Dialog;
using Celbridge.Settings;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class AppTools
{
    /// <summary>Schedule an automated answer for the next modal dialog (debug-only test automation).</summary>
    [McpServerTool(Name = "app_answer_dialog", ReadOnly = false, Idempotent = false)]
    [ToolAlias("app.answer_dialog")]
    [RelatedGuides]
    public partial CallToolResult AnswerDialog(string dialogKind, string payload = "", int delayMs = 250)
    {
        var featureFlags = GetRequiredService<IFeatureFlags>();
        if (!featureFlags.IsEnabled(FeatureFlagConstants.AnswerDialog))
        {
            return ToolResponse.FeatureFlagDisabled(FeatureFlagConstants.AnswerDialog);
        }

        if (!Enum.TryParse<DialogKind>(dialogKind, ignoreCase: false, out var kind))
        {
            var validNames = string.Join(", ", Enum.GetNames<DialogKind>());
            return ToolResponse.Error($"Invalid dialogKind '{dialogKind}'. Valid values: {validNames}.");
        }

        var dialogService = GetRequiredService<IDialogService>();
        dialogService.ScheduleAnswer(kind, payload, delayMs);

        return ToolResponse.Success("ok");
    }
}
#endif
