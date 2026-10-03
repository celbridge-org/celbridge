using Celbridge.Dialog;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class UITools
{
    /// <summary>Schedule an automated answer for the next modal dialog (debug builds only).</summary>
    [McpServerTool(Name = "ui_answer_dialog", ReadOnly = false, Idempotent = false)]
    [ToolAlias("ui.answer_dialog")]
    [RelatedGuides]
    public partial CallToolResult AnswerDialog(string dialogKind, string payload = "", int delayMs = 250)
    {
#if DEBUG
        if (!Enum.TryParse<DialogKind>(dialogKind, ignoreCase: false, out var kind))
        {
            var validNames = string.Join(", ", Enum.GetNames<DialogKind>());
            return ToolResponse.Error($"Invalid dialogKind '{dialogKind}'. Valid values: {validNames}.");
        }

        var dialogService = GetRequiredService<IDialogService>();
        dialogService.ScheduleAnswer(kind, payload, delayMs);

        return ToolResponse.Success("ok");
#else
        // The tool stays declared so its guide stays paired with a registered tool.
        return ToolResponse.Error("ui_answer_dialog is available in debug builds only.");
#endif
    }
}
